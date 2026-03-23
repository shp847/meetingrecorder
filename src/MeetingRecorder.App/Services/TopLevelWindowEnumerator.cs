using System.Runtime.InteropServices;
using System.Text;

namespace MeetingRecorder.App.Services;

internal readonly record struct TopLevelWindowSnapshot(
    nint WindowHandle,
    int ProcessId,
    string WindowTitle,
    string WindowClassName);

internal static class TopLevelWindowEnumerator
{
    private delegate bool EnumWindowsProc(nint windowHandle, nint lParam);

    public static IReadOnlyList<TopLevelWindowSnapshot> EnumerateVisibleWindowsByClass(
        IReadOnlyList<string> windowClassNames)
    {
        ArgumentNullException.ThrowIfNull(windowClassNames);

        var windows = new List<TopLevelWindowSnapshot>();
        foreach (var windowClassName in windowClassNames)
        {
            if (string.IsNullOrWhiteSpace(windowClassName))
            {
                continue;
            }

            var currentWindow = nint.Zero;
            while (true)
            {
                currentWindow = FindWindowExW(nint.Zero, currentWindow, windowClassName, null);
                if (currentWindow == nint.Zero)
                {
                    break;
                }

                if (!IsWindowVisible(currentWindow))
                {
                    continue;
                }

                GetWindowThreadProcessId(currentWindow, out var processId);
                windows.Add(new TopLevelWindowSnapshot(
                    currentWindow,
                    unchecked((int)processId),
                    GetWindowTitle(currentWindow),
                    windowClassName));
            }
        }

        return windows;
    }

    public static IReadOnlyList<TopLevelWindowSnapshot> EnumerateVisibleWindows(
        Func<int, string, bool>? shouldIncludeWindow = null)
    {
        var windows = new List<TopLevelWindowSnapshot>();
        _ = EnumWindows((windowHandle, _) =>
        {
            if (!IsWindowVisible(windowHandle))
            {
                return true;
            }

            GetWindowThreadProcessId(windowHandle, out var processId);
            var normalizedProcessId = unchecked((int)processId);
            var windowClassName = GetWindowClassName(windowHandle);
            if (shouldIncludeWindow is not null &&
                !shouldIncludeWindow(normalizedProcessId, windowClassName))
            {
                return true;
            }

            windows.Add(new TopLevelWindowSnapshot(
                windowHandle,
                normalizedProcessId,
                GetWindowTitle(windowHandle),
                windowClassName));
            return true;
        }, nint.Zero);

        return windows;
    }

    private static string GetWindowTitle(nint windowHandle)
    {
        var length = GetWindowTextLengthW(windowHandle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowTextW(windowHandle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetWindowClassName(nint windowHandle)
    {
        var builder = new StringBuilder(256);
        var copiedLength = GetClassNameW(windowHandle, builder, builder.Capacity);
        return copiedLength <= 0 ? string.Empty : builder.ToString();
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(nint windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint windowHandle, StringBuilder buffer, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint windowHandle, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowExW(nint parentWindowHandle, nint childAfterWindowHandle, string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);
}
