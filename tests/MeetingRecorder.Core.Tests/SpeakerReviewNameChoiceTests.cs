using MeetingRecorder.App;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SpeakerReviewNameChoiceTests
{
    [Fact]
    public void BuildSpeakerReviewNameChoices_Uses_Local_Safe_Sources_And_Bounds_Result()
    {
        var meeting = new MeetingOutputRecord(
            "meeting",
            "Meeting",
            DateTimeOffset.UtcNow,
            MeetingPlatform.Teams,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [new MeetingAttendee("Jane Smith", [MeetingAttendeeSource.OutlookCalendar])],
            false,
            null,
            KeyAttendees: ["Alex Lee", "jane smith"]);
        var reviewRows = SpeakerReviewSnapshotResolver.Resolve(new SpeakerReviewSnapshotInput(
            "meeting",
            "revision",
            [
                new SpeakerIdentity("one", "Speaker 1", false),
                new SpeakerIdentity("two", "Morgan Yu", true, NameSource: SpeakerNameSource.UserEdited),
            ],
            new HashSet<string>(StringComparer.Ordinal),
            SpeakerNameLearningMode.LocalAutoLearn,
            true,
            false,
            false)).Rows;

        var choices = MainWindow.BuildSpeakerReviewNameChoices(
            meeting,
            reviewRows,
            ["Jordan Kim", "Alex Lee", "Speaker 2"]);

        Assert.Equal(["Alex Lee", "Jane Smith", "Jordan Kim", "Morgan Yu"], choices);
    }

    [Fact]
    public void BuildSpeakerReviewNameChoices_Caps_Profile_Suggestions_And_Excludes_Generic_Names()
    {
        var meeting = new MeetingOutputRecord(
            "meeting",
            "Meeting",
            DateTimeOffset.UtcNow,
            MeetingPlatform.Teams,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            false,
            null);

        var choices = MainWindow.BuildSpeakerReviewNameChoices(
            meeting,
            [],
            Enumerable.Range(0, 30).Select(index => index == 0 ? "Speaker 1" : $"Profile {index:D2}"));

        Assert.Equal(24, choices.Count);
        Assert.DoesNotContain("Speaker 1", choices);
        Assert.Equal("Profile 01", choices[0]);
        Assert.Equal("Profile 24", choices[^1]);
    }

    [Fact]
    public void BuildSpeakerReviewNameChoices_Tolerates_Legacy_Meetings_Without_Attendee_Metadata()
    {
        var meeting = new MeetingOutputRecord(
            "meeting",
            "Meeting",
            DateTimeOffset.UtcNow,
            MeetingPlatform.Teams,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null!,
            false,
            null);

        var choices = MainWindow.BuildSpeakerReviewNameChoices(meeting, [], ["Morgan Yu"]);

        Assert.Equal(["Morgan Yu"], choices);
    }
}
