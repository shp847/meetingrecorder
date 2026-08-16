namespace MeetingRecorder.App.Services;

internal sealed record TeamsRecordingPlaybackProbeResult(string RecordingId);

internal interface ITeamsRecordingPlaybackProbe
{
    TeamsRecordingPlaybackProbeResult? TryProbe(MeetingWindowCandidate candidate);
}

internal sealed class TeamsRecordingPlaybackProbe : ITeamsRecordingPlaybackProbe
{
    private readonly ITeamsAutomationNodeSource _nodeSource;

    public TeamsRecordingPlaybackProbe()
        : this(new TeamsUiAutomationRosterSource.TeamsAutomationNodeSource())
    {
    }

    internal TeamsRecordingPlaybackProbe(ITeamsAutomationNodeSource nodeSource)
    {
        _nodeSource = nodeSource ?? throw new ArgumentNullException(nameof(nodeSource));
    }

    public TeamsRecordingPlaybackProbeResult? TryProbe(MeetingWindowCandidate candidate)
    {
        if (candidate.WindowHandle == nint.Zero ||
            !string.Equals(candidate.WindowClassName, "TeamsWebView", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return TeamsRecordingPlaybackProbeParser.TryExtract(_nodeSource.TryBuildNode(candidate.WindowHandle));
    }
}

internal static class TeamsRecordingPlaybackProbeParser
{
    public static TeamsRecordingPlaybackProbeResult? TryExtract(TeamsAutomationNode? root)
    {
        if (root is null)
        {
            return null;
        }

        var nodes = Enumerate(root).ToArray();
        var hasRecordingVideo = nodes.Any(node =>
            node.Name.Contains("Meeting recording video", StringComparison.OrdinalIgnoreCase) ||
            node.AutomationId.Contains("streamEmbedVideoContainer", StringComparison.OrdinalIgnoreCase));
        var hasMediaPlayer = nodes.Any(node =>
            node.Name.StartsWith("Media player", StringComparison.OrdinalIgnoreCase) ||
            node.Name.Contains("Media playback controls", StringComparison.OrdinalIgnoreCase) ||
            node.Name.Contains("Player controls", StringComparison.OrdinalIgnoreCase));
        var hasProgressBar = nodes.Any(node =>
            string.Equals(node.ControlType, "ControlType.Slider", StringComparison.OrdinalIgnoreCase) &&
            node.Name.Contains("Progress bar", StringComparison.OrdinalIgnoreCase));
        if (!hasRecordingVideo || !hasMediaPlayer || !hasProgressBar)
        {
            return null;
        }

        foreach (var node in nodes)
        {
            if (!node.ClassName.Contains("critical-playback-container", StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParse(node.AutomationId, out var recordingId))
            {
                continue;
            }

            return new TeamsRecordingPlaybackProbeResult(recordingId.ToString("D"));
        }

        return null;
    }

    private static IEnumerable<TeamsAutomationNode> Enumerate(TeamsAutomationNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in Enumerate(child))
            {
                yield return descendant;
            }
        }
    }
}
