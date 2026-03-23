using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using System.Diagnostics;
using System.Windows.Automation;

namespace MeetingRecorder.App.Services;

internal interface ITeamsRosterAutomationSource
{
    IReadOnlyList<string> CaptureParticipantNames();
}

internal readonly record struct TeamsAutomationWindowCandidate(
    string ProcessName,
    nint WindowHandle);

internal interface ITeamsAutomationWindowSource
{
    IReadOnlyList<TeamsAutomationWindowCandidate> EnumerateWindowCandidates();
}

internal interface ITeamsAutomationNodeSource
{
    TeamsAutomationNode? TryBuildNode(nint windowHandle);
}

internal sealed record TeamsAutomationNode(
    string Name,
    string AutomationId,
    string ClassName,
    string ControlType,
    IReadOnlyList<TeamsAutomationNode> Children);

internal static class TeamsAutomationNodeParser
{
    private static readonly string[] RosterKeywords =
    [
        "participants",
        "people",
        "attendees",
        "roster",
        "in this meeting",
        "meeting participants",
    ];

    private static readonly HashSet<string> AllowedControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ControlType.ListItem",
        "ControlType.Text",
        "ControlType.DataItem",
        "ControlType.Custom",
    };

    private static readonly string[] IgnoredNameFragments =
    [
        "participants",
        "people",
        "attendees",
        "roster",
        "search",
        "invite",
        "meeting chat",
        "mute all",
        "add people",
        "more options",
        "show participants",
        "in this meeting",
        "leave",
        "join",
        "call",
        "raise hand",
        "present",
        "camera",
        "microphone",
        "speaker",
        "chat",
        "apps",
        "reactions",
    ];

    public static IReadOnlyList<string> ExtractParticipantNames(TeamsAutomationNode root)
    {
        var rosterSubtrees = new List<TeamsAutomationNode>();
        CollectRosterSubtrees(root, rosterSubtrees);
        if (rosterSubtrees.Count == 0)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rosterSubtree in rosterSubtrees)
        {
            CollectParticipantNames(rosterSubtree, seen, names);
        }

        return names;
    }

    private static void CollectRosterSubtrees(TeamsAutomationNode node, ICollection<TeamsAutomationNode> rosterSubtrees)
    {
        if (LooksLikeRosterContainer(node))
        {
            rosterSubtrees.Add(node);
        }

        foreach (var child in node.Children)
        {
            CollectRosterSubtrees(child, rosterSubtrees);
        }
    }

    private static bool LooksLikeRosterContainer(TeamsAutomationNode node)
    {
        var haystack = string.Join(
            " ",
            new[]
            {
                node.Name,
                node.AutomationId,
                node.ClassName,
            })
            .ToLowerInvariant();

        return RosterKeywords.Any(keyword => haystack.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static void CollectParticipantNames(
        TeamsAutomationNode node,
        ISet<string> seen,
        ICollection<string> names)
    {
        if (IsParticipantCandidate(node))
        {
            var normalizedName = node.Name.Trim();
            if (seen.Add(normalizedName))
            {
                names.Add(normalizedName);
            }
        }

        foreach (var child in node.Children)
        {
            CollectParticipantNames(child, seen, names);
        }
    }

    private static bool IsParticipantCandidate(TeamsAutomationNode node)
    {
        var trimmedName = node.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return false;
        }

        if (!AllowedControlTypes.Contains(node.ControlType))
        {
            return false;
        }

        if (trimmedName.Length < 2 || trimmedName.Length > 120)
        {
            return false;
        }

        if (IgnoredNameFragments.Any(fragment => trimmedName.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (trimmedName.All(character => !char.IsLetter(character)))
        {
            return false;
        }

        return true;
    }
}

internal sealed class TeamsLiveAttendeeCaptureService
{
    private readonly ITeamsRosterAutomationSource _automationSource;
    private readonly bool _isEnabled;

    public TeamsLiveAttendeeCaptureService()
        : this(new TeamsUiAutomationRosterSource(), isEnabled: false)
    {
    }

    internal TeamsLiveAttendeeCaptureService(
        ITeamsRosterAutomationSource automationSource,
        bool isEnabled)
    {
        _automationSource = automationSource;
        _isEnabled = isEnabled;
    }

    public async Task<IReadOnlyList<MeetingAttendee>> TryCaptureAttendeesAsync(CancellationToken cancellationToken = default)
    {
        if (!_isEnabled)
        {
            return Array.Empty<MeetingAttendee>();
        }

        try
        {
            var participantNames = await RunOnStaThreadAsync(
                () => _automationSource.CaptureParticipantNames(),
                cancellationToken);
            return NormalizeCapturedAttendees(participantNames);
        }
        catch
        {
            return Array.Empty<MeetingAttendee>();
        }
    }

    internal static async Task<MeetingSessionManifest> MergeAttendeesIntoManifestAsync(
        SessionManifestStore manifestStore,
        MeetingSessionManifest manifest,
        string manifestPath,
        IReadOnlyList<MeetingAttendee> discoveredAttendees,
        CancellationToken cancellationToken = default)
    {
        var mergedAttendees = MergeAttendees(manifest.Attendees, discoveredAttendees);
        var mergedKeyAttendees = MeetingMetadataNameMatcher.MergeNames(
            manifest.KeyAttendees,
            discoveredAttendees.Select(attendee => attendee.Name).ToArray());
        if (AreEquivalent(manifest.Attendees, mergedAttendees) &&
            (manifest.KeyAttendees ?? Array.Empty<string>()).SequenceEqual(mergedKeyAttendees, StringComparer.Ordinal))
        {
            return manifest;
        }

        var updatedManifest = manifest with
        {
            Attendees = mergedAttendees,
            KeyAttendees = mergedKeyAttendees,
        };
        await manifestStore.SaveAsync(updatedManifest, manifestPath, cancellationToken);
        return updatedManifest;
    }

    internal static IReadOnlyList<MeetingAttendee> MergeAttendees(
        IReadOnlyList<MeetingAttendee> existingAttendees,
        IReadOnlyList<MeetingAttendee> discoveredAttendees)
    {
        var merged = new List<(string Name, HashSet<MeetingAttendeeSource> Sources)>();

        AddAttendees(existingAttendees, merged);
        AddAttendees(discoveredAttendees, merged);

        return merged
            .Select(item => new MeetingAttendee(
                item.Name,
                item.Sources
                    .OrderBy(GetSourcePriority)
                    .ThenBy(source => source.ToString(), StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();
    }

    private static void AddAttendees(
        IReadOnlyList<MeetingAttendee> attendees,
        IList<(string Name, HashSet<MeetingAttendeeSource> Sources)> merged)
    {
        foreach (var attendee in attendees)
        {
            if (string.IsNullOrWhiteSpace(attendee.Name))
            {
                continue;
            }

            var trimmedName = MeetingMetadataNameMatcher.NormalizeDisplayName(attendee.Name);
            var existingIndex = FindAttendeeIndex(merged, trimmedName);
            if (existingIndex < 0)
            {
                var newSources = new HashSet<MeetingAttendeeSource>();
                foreach (var source in attendee.Sources)
                {
                    newSources.Add(source);
                }

                if (newSources.Count == 0)
                {
                    newSources.Add(MeetingAttendeeSource.Unknown);
                }

                merged.Add((trimmedName, newSources));
                continue;
            }

            var existing = merged[existingIndex];
            var existingSources = existing.Sources;
            foreach (var source in attendee.Sources)
            {
                existingSources.Add(source);
            }

            if (existingSources.Count == 0)
            {
                existingSources.Add(MeetingAttendeeSource.Unknown);
            }

            merged[existingIndex] = (
                MeetingMetadataNameMatcher.ChoosePreferredDisplayName(existing.Name, trimmedName),
                existingSources);
        }
    }

    private static int FindAttendeeIndex(
        IList<(string Name, HashSet<MeetingAttendeeSource> Sources)> merged,
        string candidateName)
    {
        for (var index = 0; index < merged.Count; index++)
        {
            if (MeetingMetadataNameMatcher.AreReasonableMatch(merged[index].Name, candidateName))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool AreEquivalent(
        IReadOnlyList<MeetingAttendee> existingAttendees,
        IReadOnlyList<MeetingAttendee> mergedAttendees)
    {
        if (existingAttendees.Count != mergedAttendees.Count)
        {
            return false;
        }

        for (var index = 0; index < existingAttendees.Count; index++)
        {
            var existing = existingAttendees[index];
            var merged = mergedAttendees[index];
            if (!string.Equals(existing.Name, merged.Name, StringComparison.Ordinal) ||
                !existing.Sources.SequenceEqual(merged.Sources))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<MeetingAttendee> NormalizeCapturedAttendees(IReadOnlyList<string> participantNames)
    {
        if (participantNames.Count == 0)
        {
            return Array.Empty<MeetingAttendee>();
        }

        var orderedNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var participantName in participantNames)
        {
            if (string.IsNullOrWhiteSpace(participantName))
            {
                continue;
            }

            var trimmedName = participantName.Trim();
            if (seen.Add(trimmedName))
            {
                orderedNames.Add(trimmedName);
            }
        }

        return orderedNames
            .Select(name => new MeetingAttendee(name, [MeetingAttendeeSource.TeamsLiveRoster]))
            .ToArray();
    }

    private static int GetSourcePriority(MeetingAttendeeSource source)
    {
        return source switch
        {
            MeetingAttendeeSource.TeamsLiveRoster => 0,
            MeetingAttendeeSource.OutlookCalendar => 1,
            _ => 2,
        };
    }

    private static Task<T> RunOnStaThreadAsync<T>(Func<T> workItem, CancellationToken cancellationToken)
    {
        return StaThreadRunner.RunAsync(workItem, cancellationToken);
    }

}

internal sealed class TeamsUiAutomationRosterSource : ITeamsRosterAutomationSource
{
    private static readonly string[] SupportedTeamsProcessNames =
    [
        "teams",
        "ms-teams",
    ];

    private readonly ITeamsAutomationWindowSource _windowSource;
    private readonly ITeamsAutomationNodeSource _nodeSource;

    public TeamsUiAutomationRosterSource()
        : this(new TeamsProcessWindowSource(), new TeamsAutomationNodeSource())
    {
    }

    internal TeamsUiAutomationRosterSource(
        ITeamsAutomationWindowSource windowSource,
        ITeamsAutomationNodeSource nodeSource)
    {
        _windowSource = windowSource ?? throw new ArgumentNullException(nameof(windowSource));
        _nodeSource = nodeSource ?? throw new ArgumentNullException(nameof(nodeSource));
    }

    public IReadOnlyList<string> CaptureParticipantNames()
    {
        var participantNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var windowCandidate in _windowSource.EnumerateWindowCandidates())
        {
            if (!IsSupportedTeamsProcessName(windowCandidate.ProcessName))
            {
                continue;
            }

            var rootNode = _nodeSource.TryBuildNode(windowCandidate.WindowHandle);
            if (rootNode is null)
            {
                continue;
            }

            var extractedNames = TeamsAutomationNodeParser.ExtractParticipantNames(rootNode);
            foreach (var extractedName in extractedNames)
            {
                if (seen.Add(extractedName))
                {
                    participantNames.Add(extractedName);
                }
            }
        }

        return participantNames;
    }

    internal static bool IsSupportedTeamsProcessName(string processName)
    {
        return SupportedTeamsProcessNames.Any(name =>
            processName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TeamsProcessWindowSource : ITeamsAutomationWindowSource
    {
        public IReadOnlyList<TeamsAutomationWindowCandidate> EnumerateWindowCandidates()
        {
            var candidates = new List<TeamsAutomationWindowCandidate>();
            foreach (var processName in SupportedTeamsProcessNames)
            {
                Process[] processes;
                try
                {
                    processes = Process.GetProcessesByName(processName);
                }
                catch
                {
                    continue;
                }

                foreach (var process in processes)
                {
                    try
                    {
                        if (process.MainWindowHandle == nint.Zero)
                        {
                            continue;
                        }

                        candidates.Add(new TeamsAutomationWindowCandidate(
                            process.ProcessName,
                            process.MainWindowHandle));
                    }
                    catch
                    {
                        // System-managed windows can disappear while they are being enumerated.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            return candidates;
        }
    }

    private sealed class TeamsAutomationNodeSource : ITeamsAutomationNodeSource
    {
        private const int MaxTraversalDepth = 6;
        private const int MaxTraversalNodes = 1200;

        public TeamsAutomationNode? TryBuildNode(nint windowHandle)
        {
            if (windowHandle == nint.Zero)
            {
                return null;
            }

            try
            {
                var element = AutomationElement.FromHandle(windowHandle);
                var nodeCount = 0;
                return BuildNode(element, depth: 0, ref nodeCount);
            }
            catch
            {
                return null;
            }
        }

        private static TeamsAutomationNode BuildNode(AutomationElement element, int depth, ref int nodeCount)
        {
            nodeCount++;
            var childNodes = new List<TeamsAutomationNode>();

            if (depth < MaxTraversalDepth && nodeCount < MaxTraversalNodes)
            {
                AutomationElementCollection? children = null;
                try
                {
                    children = element.FindAll(TreeScope.Children, Condition.TrueCondition);
                }
                catch
                {
                    children = null;
                }

                if (children is not null)
                {
                    foreach (AutomationElement child in children)
                    {
                        if (nodeCount >= MaxTraversalNodes)
                        {
                            break;
                        }

                        childNodes.Add(BuildNode(child, depth + 1, ref nodeCount));
                    }
                }
            }

            return new TeamsAutomationNode(
                SafeGet(() => element.Current.Name),
                SafeGet(() => element.Current.AutomationId),
                SafeGet(() => element.Current.ClassName),
                SafeGet(() => element.Current.ControlType.ProgrammaticName),
                childNodes);
        }

        private static string SafeGet(Func<string> getter)
        {
            try
            {
                return getter() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
