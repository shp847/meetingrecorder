using System.Buffers.Binary;

namespace MeetingRecorder.Core.Services;

public enum WavInputDisposition
{
    Valid = 0,
    NormalizeTemporaryCopy = 1,
    ManualReview = 2,
}

public sealed record WavInputInspection(
    WavInputDisposition Disposition,
    long EffectiveDataBytes,
    long PhysicalDataBytes,
    int ByteRate,
    int BlockAlign,
    TimeSpan EffectiveDuration,
    string? DiagnosticMessage);

public sealed class WavInputManualReviewException : IOException
{
    public WavInputManualReviewException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Validates PCM RIFF boundaries before NAudio is asked to decode the file.
/// A recording is never changed in place; repairable headers are corrected only in a temporary copy.
/// </summary>
public sealed class WavInputInspector
{
    public WavInputInspection Inspect(string audioPath)
    {
        if (!File.Exists(audioPath))
        {
            throw new FileNotFoundException("The source audio file does not exist.", audioPath);
        }

        using var stream = new FileStream(audioPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 44)
        {
            throw new WavInputManualReviewException("Speaker labeling requires manual review because the WAV file is too short to contain a valid RIFF header.");
        }

        Span<byte> header = stackalloc byte[12];
        ReadExactly(stream, header);
        if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..12].SequenceEqual("WAVE"u8))
        {
            // Non-WAV formats remain supported by the decoder path; only RIFF files receive strict repair handling.
            return new WavInputInspection(WavInputDisposition.Valid, 0, 0, 0, 0, TimeSpan.Zero, null);
        }

        ushort formatTag = 0;
        ushort blockAlign = 0;
        uint byteRate = 0;
        long declaredDataBytes = -1;
        long dataStartOffset = -1;
        Span<byte> chunkHeader = stackalloc byte[8];
        Span<byte> format = stackalloc byte[16];

        while (stream.Position + 8 <= stream.Length)
        {
            ReadExactly(stream, chunkHeader);
            var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
            var chunkStart = stream.Position;
            var chunkEnd = chunkStart + chunkLength;
            if (chunkHeader[..4].SequenceEqual("data"u8))
            {
                declaredDataBytes = chunkLength;
                dataStartOffset = chunkStart;
                break;
            }

            if (chunkEnd > stream.Length)
            {
                var chunkName = System.Text.Encoding.ASCII.GetString(chunkHeader[..4]);
                throw new WavInputManualReviewException($"Speaker labeling requires manual review because WAV chunk '{chunkName}' extends beyond the physical file.");
            }

            if (chunkHeader[..4].SequenceEqual("fmt "u8))
            {
                if (chunkLength < 16)
                {
                    throw new WavInputManualReviewException("Speaker labeling requires manual review because the WAV format chunk is incomplete.");
                }

                ReadExactly(stream, format);
                formatTag = BinaryPrimitives.ReadUInt16LittleEndian(format);
                byteRate = BinaryPrimitives.ReadUInt32LittleEndian(format[8..]);
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(format[12..]);
            }
            stream.Position = chunkEnd + (chunkLength % 2);
        }

        if (dataStartOffset < 0)
        {
            throw new WavInputManualReviewException("Speaker labeling requires manual review because the WAV container does not contain an audio data chunk.");
        }

        if (formatTag != 1)
        {
            // Float and other decodable WAV formats are valid inputs. Only PCM is safe to repair
            // by rewriting a data length, so keep these on NAudio's established conversion path.
            return new WavInputInspection(WavInputDisposition.Valid, 0, 0, 0, 0, TimeSpan.Zero, null);
        }

        if (blockAlign == 0 || byteRate == 0)
        {
            throw new WavInputManualReviewException("Speaker labeling requires manual review because the PCM WAV format is incomplete.");
        }

        var physicalDataBytes = stream.Length - dataStartOffset;
        if (physicalDataBytes < 0 || physicalDataBytes % blockAlign != 0)
        {
            throw new WavInputManualReviewException("Speaker labeling requires manual review because the PCM payload is not aligned to its declared sample format.");
        }

        var effectiveBytes = Math.Min(declaredDataBytes, physicalDataBytes);
        var duration = TimeSpan.FromSeconds(effectiveBytes / (double)byteRate);
        if (declaredDataBytes == physicalDataBytes)
        {
            return new WavInputInspection(WavInputDisposition.Valid, effectiveBytes, physicalDataBytes, (int)byteRate, blockAlign, duration, null);
        }

        if (declaredDataBytes > physicalDataBytes)
        {
            return new WavInputInspection(
                WavInputDisposition.NormalizeTemporaryCopy,
                physicalDataBytes,
                physicalDataBytes,
                (int)byteRate,
                blockAlign,
                TimeSpan.FromSeconds(physicalDataBytes / (double)byteRate),
                $"WAV header declared {FormatBytes(declaredDataBytes)} of PCM data, but the file contains {FormatBytes(physicalDataBytes)}. A temporary normalized copy will be used.");
        }

        throw new WavInputManualReviewException(
            "Speaker labeling requires manual review because the WAV header ends its data chunk before additional physical audio bytes. The recorder will not guess where a later RIFF chunk begins.");
    }

    public async Task<string> CreateNormalizedTemporaryCopyAsync(
        string sourceAudioPath,
        string normalizedPath,
        WavInputInspection inspection,
        CancellationToken cancellationToken = default)
    {
        if (inspection.Disposition != WavInputDisposition.NormalizeTemporaryCopy)
        {
            return sourceAudioPath;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(normalizedPath) ?? throw new InvalidOperationException("A normalized WAV path must include a directory."));
        await using var source = new FileStream(sourceAudioPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var destination = new FileStream(normalizedPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        await source.CopyToAsync(destination, cancellationToken);

        destination.Position = 4;
        await WriteUInt32Async(destination, checked((uint)(destination.Length - 8)), cancellationToken);

        // The inspector only offers repair when the data chunk is the final chunk and physical payload is aligned.
        destination.Position = FindDataSizeOffset(destination);
        await WriteUInt32Async(destination, checked((uint)inspection.EffectiveDataBytes), cancellationToken);
        await destination.FlushAsync(cancellationToken);
        return normalizedPath;
    }

    private static long FindDataSizeOffset(Stream stream)
    {
        stream.Position = 12;
        Span<byte> chunkHeader = stackalloc byte[8];
        while (stream.Position + 8 <= stream.Length)
        {
            ReadExactly(stream, chunkHeader);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
            if (chunkHeader[..4].SequenceEqual("data"u8))
            {
                return stream.Position - 4;
            }

            stream.Position += length + (length % 2);
        }

        throw new InvalidDataException("The normalized WAV copy did not contain a data chunk.");
    }

    private static async Task WriteUInt32Async(Stream stream, uint value, CancellationToken cancellationToken)
    {
        var bytes = BitConverter.GetBytes(value);
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer[offset..]);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of WAV file.");
            }

            offset += read;
        }
    }

    private static string FormatBytes(long value) => $"{value / (1024d * 1024d):0.0} MB";
}
