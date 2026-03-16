using NAudio.Wave;

namespace MeetingRecorder.Core.Services;

public sealed class WaveChunkMerger
{
    public Task<string> MergeAsync(
        IReadOnlyList<string> chunkPaths,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (chunkPaths.Count == 0)
        {
            throw new InvalidOperationException("At least one wave chunk is required.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException("Output path must have a directory."));
        cancellationToken.ThrowIfCancellationRequested();

        using var firstReader = new WaveFileReader(chunkPaths[0]);
        using var writer = new WaveFileWriter(outputPath, firstReader.WaveFormat);
        CopyReader(firstReader, writer, cancellationToken);

        for (var index = 1; index < chunkPaths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var nextReader = new WaveFileReader(chunkPaths[index]);
            if (!nextReader.WaveFormat.Equals(firstReader.WaveFormat))
            {
                throw new InvalidOperationException("Wave chunks must share the same format to be merged.");
            }

            CopyReader(nextReader, writer, cancellationToken);
        }

        return Task.FromResult(outputPath);
    }

    private static void CopyReader(WaveFileReader reader, WaveFileWriter writer, CancellationToken cancellationToken)
    {
        var buffer = new byte[16384];
        int bytesRead;
        while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.Write(buffer, 0, bytesRead);
        }
    }
}
