using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class TeamsLiveAttendeeCaptureServiceTests
{
    [Fact]
    public async Task TryCaptureAttendeesAsync_Returns_Empty_When_LiveCapture_Is_Disabled()
    {
        var automationSource = new RecordingTeamsRosterAutomationSource(["Jane Smith"]);
        var service = new TeamsLiveAttendeeCaptureService(automationSource, isEnabled: false);

        var attendees = await service.TryCaptureAttendeesAsync();

        Assert.Empty(attendees);
        Assert.Equal(0, automationSource.CallCount);
    }

    [Fact]
    public void CaptureParticipantNames_Ignores_Unsupported_Window_Candidates()
    {
        var nodeSource = new RecordingTeamsAutomationNodeSource(
            new Dictionary<nint, TeamsAutomationNode?>
            {
                [(nint)2] = new TeamsAutomationNode(
                    "Contoso Weekly Sync | Microsoft Teams",
                    string.Empty,
                    "Chrome_WidgetWin_1",
                    "ControlType.Window",
                    [
                        new TeamsAutomationNode(
                            "Participants",
                            "ParticipantsPane",
                            "Pane",
                            "ControlType.Pane",
                            [
                                new TeamsAutomationNode("Jane Smith", string.Empty, "ListViewItem", "ControlType.ListItem", Array.Empty<TeamsAutomationNode>()),
                            ]),
                    ]),
            });
        var rosterSource = new TeamsUiAutomationRosterSource(
            new StubTeamsAutomationWindowSource(
                new TeamsAutomationWindowCandidate("WerFault", (nint)1),
                new TeamsAutomationWindowCandidate("ms-teams", (nint)2)),
            nodeSource);

        var names = rosterSource.CaptureParticipantNames();

        Assert.Equal(["Jane Smith"], names);
        Assert.Equal([(nint)2], nodeSource.RequestedHandles);
    }

    [Fact]
    public void ExtractParticipantNames_Returns_Attendee_Names_From_Roster_Subtree()
    {
        var root = new TeamsAutomationNode(
            "Contoso Weekly Sync | Microsoft Teams",
            string.Empty,
            "Chrome_WidgetWin_1",
            "ControlType.Window",
            [
                new TeamsAutomationNode(
                    "Participants",
                    "ParticipantsPane",
                    "Pane",
                    "ControlType.Pane",
                    [
                        new TeamsAutomationNode("Participants", string.Empty, "TextBlock", "ControlType.Text", Array.Empty<TeamsAutomationNode>()),
                        new TeamsAutomationNode(
                            "In this meeting",
                            string.Empty,
                            "List",
                            "ControlType.List",
                            [
                                new TeamsAutomationNode("Jane Smith", string.Empty, "ListViewItem", "ControlType.ListItem", Array.Empty<TeamsAutomationNode>()),
                                new TeamsAutomationNode("John Doe", string.Empty, "ListViewItem", "ControlType.ListItem", Array.Empty<TeamsAutomationNode>()),
                                new TeamsAutomationNode("Mute all", string.Empty, "Button", "ControlType.Button", Array.Empty<TeamsAutomationNode>()),
                                new TeamsAutomationNode("Search participants", string.Empty, "TextBox", "ControlType.Edit", Array.Empty<TeamsAutomationNode>()),
                            ]),
                    ]),
            ]);

        var names = TeamsAutomationNodeParser.ExtractParticipantNames(root);

        Assert.Equal(["Jane Smith", "John Doe"], names);
    }

    [Fact]
    public async Task TryCaptureAttendeesAsync_SoftFails_When_Automation_Throws()
    {
        var service = new TeamsLiveAttendeeCaptureService(new ThrowingTeamsRosterAutomationSource(), isEnabled: true);

        var attendees = await service.TryCaptureAttendeesAsync();

        Assert.Empty(attendees);
    }

    [Fact]
    public void MergeAttendees_Deduplicates_Names_And_Prefers_Teams_Live_Source_Order()
    {
        var merged = TeamsLiveAttendeeCaptureService.MergeAttendees(
            [new MeetingAttendee("Jane Smith", [MeetingAttendeeSource.OutlookCalendar])],
            [
                new MeetingAttendee(" jane smith ", [MeetingAttendeeSource.TeamsLiveRoster]),
                new MeetingAttendee("John Doe", [MeetingAttendeeSource.TeamsLiveRoster]),
            ]);

        Assert.Collection(merged,
            attendee =>
            {
                Assert.Equal("Jane Smith", attendee.Name);
                Assert.Equal(
                    [MeetingAttendeeSource.TeamsLiveRoster, MeetingAttendeeSource.OutlookCalendar],
                    attendee.Sources);
            },
            attendee =>
            {
                Assert.Equal("John Doe", attendee.Name);
                Assert.Equal([MeetingAttendeeSource.TeamsLiveRoster], attendee.Sources);
            });
    }

    [Fact]
    public async Task MergeAttendeesIntoManifestAsync_Preserves_Existing_Attendee_Data()
    {
        var root = Path.Combine(Path.GetTempPath(), $"teams-attendee-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var pathBuilder = new ArtifactPathBuilder();
            var manifestStore = new SessionManifestStore(pathBuilder);
            var manifestPath = Path.Combine(root, "manifest.json");
            var manifest = new MeetingSessionManifest
            {
                SessionId = "session-001",
                Platform = MeetingPlatform.Teams,
                DetectedTitle = "Weekly Sync",
                StartedAtUtc = DateTimeOffset.Parse("2026-03-21T13:00:00Z"),
                Attendees =
                [
                    new MeetingAttendee("Jane Smith", [MeetingAttendeeSource.OutlookCalendar]),
                ],
            };
            await manifestStore.SaveAsync(manifest, manifestPath);

            var updated = await TeamsLiveAttendeeCaptureService.MergeAttendeesIntoManifestAsync(
                manifestStore,
                manifest,
                manifestPath,
                [
                    new MeetingAttendee("Jane Smith", [MeetingAttendeeSource.TeamsLiveRoster]),
                    new MeetingAttendee("John Doe", [MeetingAttendeeSource.TeamsLiveRoster]),
                ]);
            var reloaded = await manifestStore.LoadAsync(manifestPath);

            Assert.Collection(updated.Attendees,
                attendee =>
                {
                    Assert.Equal("Jane Smith", attendee.Name);
                    Assert.Equal(
                        [MeetingAttendeeSource.TeamsLiveRoster, MeetingAttendeeSource.OutlookCalendar],
                        attendee.Sources);
                },
                attendee =>
                {
                    Assert.Equal("John Doe", attendee.Name);
                    Assert.Equal([MeetingAttendeeSource.TeamsLiveRoster], attendee.Sources);
                });
            Assert.Collection(reloaded.Attendees,
                attendee =>
                {
                    Assert.Equal("Jane Smith", attendee.Name);
                    Assert.Equal(
                        [MeetingAttendeeSource.TeamsLiveRoster, MeetingAttendeeSource.OutlookCalendar],
                        attendee.Sources);
                },
                attendee =>
                {
                    Assert.Equal("John Doe", attendee.Name);
                    Assert.Equal([MeetingAttendeeSource.TeamsLiveRoster], attendee.Sources);
                });
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }
    }

    private sealed class ThrowingTeamsRosterAutomationSource : ITeamsRosterAutomationSource
    {
        public IReadOnlyList<string> CaptureParticipantNames()
        {
            throw new InvalidOperationException("UI Automation is unavailable.");
        }
    }

    private sealed class RecordingTeamsRosterAutomationSource : ITeamsRosterAutomationSource
    {
        private readonly IReadOnlyList<string> _participantNames;

        public RecordingTeamsRosterAutomationSource(IReadOnlyList<string> participantNames)
        {
            _participantNames = participantNames;
        }

        public int CallCount { get; private set; }

        public IReadOnlyList<string> CaptureParticipantNames()
        {
            CallCount++;
            return _participantNames;
        }
    }

    private sealed class StubTeamsAutomationWindowSource : ITeamsAutomationWindowSource
    {
        private readonly IReadOnlyList<TeamsAutomationWindowCandidate> _candidates;

        public StubTeamsAutomationWindowSource(params TeamsAutomationWindowCandidate[] candidates)
        {
            _candidates = candidates;
        }

        public IReadOnlyList<TeamsAutomationWindowCandidate> EnumerateWindowCandidates()
        {
            return _candidates;
        }
    }

    private sealed class RecordingTeamsAutomationNodeSource : ITeamsAutomationNodeSource
    {
        private readonly IReadOnlyDictionary<nint, TeamsAutomationNode?> _nodesByHandle;

        public RecordingTeamsAutomationNodeSource(IReadOnlyDictionary<nint, TeamsAutomationNode?> nodesByHandle)
        {
            _nodesByHandle = nodesByHandle;
        }

        public List<nint> RequestedHandles { get; } = new();

        public TeamsAutomationNode? TryBuildNode(nint windowHandle)
        {
            RequestedHandles.Add(windowHandle);
            return _nodesByHandle.TryGetValue(windowHandle, out var node)
                ? node
                : null;
        }
    }
}
