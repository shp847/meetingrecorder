using NAudio.Wave;

namespace MeetingRecorder.Core.Services;

public static class AudioLevelMeter
{
    public static double MeasurePeakLevel(byte[] buffer, int bytesRecorded, WaveFormat waveFormat)
    {
        if (buffer.Length == 0 || bytesRecorded <= 0)
        {
            return 0d;
        }

        return waveFormat.Encoding switch
        {
            WaveFormatEncoding.IeeeFloat when waveFormat.BitsPerSample == 32 =>
                MeasureFloatPeak(buffer, bytesRecorded),
            WaveFormatEncoding.Pcm when waveFormat.BitsPerSample == 16 =>
                MeasurePcm16Peak(buffer, bytesRecorded),
            WaveFormatEncoding.Pcm when waveFormat.BitsPerSample == 24 =>
                MeasurePcm24Peak(buffer, bytesRecorded),
            WaveFormatEncoding.Pcm when waveFormat.BitsPerSample == 32 =>
                MeasurePcm32Peak(buffer, bytesRecorded),
            _ => 0d,
        };
    }

    private static double MeasureFloatPeak(byte[] buffer, int bytesRecorded)
    {
        var peak = 0d;
        for (var offset = 0; offset <= bytesRecorded - sizeof(float); offset += sizeof(float))
        {
            var sample = Math.Abs(BitConverter.ToSingle(buffer, offset));
            peak = Math.Max(peak, sample);
        }

        return Math.Clamp(peak, 0d, 1d);
    }

    private static double MeasurePcm16Peak(byte[] buffer, int bytesRecorded)
    {
        var peak = 0d;
        for (var offset = 0; offset <= bytesRecorded - sizeof(short); offset += sizeof(short))
        {
            var sample = Math.Abs(BitConverter.ToInt16(buffer, offset) / 32768d);
            peak = Math.Max(peak, sample);
        }

        return Math.Clamp(peak, 0d, 1d);
    }

    private static double MeasurePcm24Peak(byte[] buffer, int bytesRecorded)
    {
        var peak = 0d;
        for (var offset = 0; offset <= bytesRecorded - 3; offset += 3)
        {
            var sample = (buffer[offset + 2] << 24) | (buffer[offset + 1] << 16) | (buffer[offset] << 8);
            sample >>= 8;
            var normalized = Math.Abs(sample / 8_388_608d);
            peak = Math.Max(peak, normalized);
        }

        return Math.Clamp(peak, 0d, 1d);
    }

    private static double MeasurePcm32Peak(byte[] buffer, int bytesRecorded)
    {
        var peak = 0d;
        for (var offset = 0; offset <= bytesRecorded - sizeof(int); offset += sizeof(int))
        {
            var sample = Math.Abs(BitConverter.ToInt32(buffer, offset) / 2147483648d);
            peak = Math.Max(peak, sample);
        }

        return Math.Clamp(peak, 0d, 1d);
    }
}
