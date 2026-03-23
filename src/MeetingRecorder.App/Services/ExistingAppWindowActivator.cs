using MeetingRecorder.Core.Branding;
using System.Runtime.InteropServices;

namespace MeetingRecorder.App.Services;

internal readonly record struct AppWindowCandidate(
    int ProcessId,
    string ProcessName,
    nint MainWindowHandle,
    string MainWindowTitle,
    string WindowClassName = "");

internal sealed class ExistingAppWindowActivator
{
    private const int SwRestore = 9;

    private readonly Func<IReadOnlyList<AppWindowCandidate>> _enumerateCandidates;
    private readonly Func<nint, bool> _activateWindow;
    private readonly Action<TimeSpan> _delay;

    public ExistingAppWindowActivator()
        : this(EnumerateCandidates, ActivateWindow, Thread.Sleep)
    {
    }

    internal ExistingAppWindowActivator(
        Func<IReadOnlyList<AppWindowCandidate>> enumerateCandidates,
        Func<nint, bool> activateWindow,
        Action<TimeSpan> delay)
    {
        _enumerateCandidates = enumerateCandidates ?? throw new ArgumentNullException(nameof(enumerateCandidates));
        _activateWindow = activateWindow ?? throw new ArgumentNullException(nameof(activateWindow));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public bool TryBringExistingWindowToFront(int currentProcessId, int maxAttempts, TimeSpan retryDelay)
    {
        if (currentProcessId <= 0 || maxAttempts <= 0)
        {
            return false;
        }

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var candidate = FindCandidateForActivation(_enumerateCandidates(), currentProcessId);
            if (candidate is { } windowCandidate &&
                _activateWindow(windowCandidate.MainWindowHandle))
            {
                return true;
            }

            if (attempt + 1 < maxAttempts && retryDelay > TimeSpan.Zero)
            {
                _delay(retryDelay);
            }
        }

        return false;
    }

    internal static AppWindowCandidate? FindCandidateForActivation(
        IEnumerable<AppWindowCandidate> candidates,
        int currentProcessId)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(candidate => candidate.ProcessId != currentProcessId)
            .Where(candidate => candidate.MainWindowHandle != nint.Zero)
            .Where(candidate => LooksLikeMeetingRecorderWindowTitle(candidate.MainWindowTitle))
            .OrderByDescending(candidate => !string.IsNullOrWhiteSpace(candidate.MainWindowTitle))
            .ThenBy(candidate => candidate.ProcessId)
            .Cast<AppWindowCandidate?>()
            .FirstOrDefault();
    }

    private static IReadOnlyList<AppWindowCandidate> EnumerateCandidates()
    {
        return TopLevelWindowEnumerator.EnumerateVisibleWindows(
                static (_, windowClassName) => LooksLikeMeetingRecorderWindowClass(windowClassName))
            .Where(window => window.WindowHandle != nint.Zero)
            .Select(window => new AppWindowCandidate(
                window.ProcessId,
                string.Empty,
                window.WindowHandle,
                window.WindowTitle,
                window.WindowClassName))
            .ToArray();
    }

    internal static bool LooksLikeMeetingRecorderWindowTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return title.Trim().StartsWith(AppBranding.ProductName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeMeetingRecorderWindowClass(string windowClassName)
    {
        return windowClassName.StartsWith("HwndWrapper[", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ActivateWindow(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            return false;
        }

        ShowWindowAsync(windowHandle, SwRestore);
        return SetForegroundWindow(windowHandle);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);
}
