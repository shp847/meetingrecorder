using System.Runtime.InteropServices;

namespace MeetingRecorder.App.Services;

/// <summary>
/// Reads only local elapsed input-idle time. It never records keys, windows, or
/// user activity content. Failure is conservative: no accelerated cleanup.
/// </summary>
internal static class WindowsUserIdleProbe
{
    public static bool TryGetIdleDuration(out TimeSpan idle)
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            idle = TimeSpan.Zero;
            return false;
        }

        var elapsedMilliseconds = unchecked((uint)Environment.TickCount - info.TickCount);
        idle = TimeSpan.FromMilliseconds(elapsedMilliseconds);
        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint TickCount;
    }
}
