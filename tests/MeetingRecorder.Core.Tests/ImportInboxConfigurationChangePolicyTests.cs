using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ImportInboxConfigurationChangePolicyTests
{
    [Fact]
    public void Evaluate_Blocks_A_Path_Change_When_An_Inbox_Receipt_Is_Active()
    {
        var current = CreateConfig("C:\\Inbox");
        var next = current with { ImportInboxDir = "C:\\NewInbox" };
        var journal = new ImportInboxJournal(
            ImportInboxJournal.CurrentSchemaVersion,
            [new ImportInboxJournalEntry
            {
                EntryId = Guid.NewGuid(),
                ObservationKey = new string('a', 64),
                RelativeSourcePath = "memo.wav",
                State = ImportInboxEntryState.Ready,
            }]);

        var decision = ImportInboxConfigurationChangePolicy.Evaluate(current, next, journal);

        Assert.False(decision.CanApply);
        Assert.DoesNotContain("C:\\", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_Allows_An_Archive_Policy_Change_After_Inbox_Work_Is_Queued()
    {
        var current = CreateConfig("C:\\Inbox");
        var next = current with { ImportInboxArchiveAfterQueueEnabled = true };
        var journal = new ImportInboxJournal(
            ImportInboxJournal.CurrentSchemaVersion,
            [new ImportInboxJournalEntry
            {
                EntryId = Guid.NewGuid(),
                ObservationKey = new string('b', 64),
                RelativeSourcePath = "memo.wav",
                State = ImportInboxEntryState.Queued,
            }]);

        var decision = ImportInboxConfigurationChangePolicy.Evaluate(current, next, journal);

        Assert.True(decision.CanApply);
    }

    private static AppConfig CreateConfig(string inboxPath) => new()
    {
        AudioOutputDir = "C:\\Recordings",
        TranscriptOutputDir = "C:\\Transcripts",
        WorkDir = "C:\\Work",
        ImportInboxDir = inboxPath,
        ImportInboxEnabled = true,
    };
}
