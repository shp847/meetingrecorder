using MeetingRecorder.Core.Services;
namespace MeetingRecorder.Core.Tests;
public sealed class MeetingArchivePreflightTests
{
    [Fact] public void Archive_Explains_Published_Scope_Without_Claiming_Work_Session_Backup() { var r=MeetingArchivePreflight.Evaluate(Input(MeetingArchiveOperation.Archive)); Assert.True(r.CanProceed); Assert.Contains("work session stays",r.Summary); Assert.False(r.IncludesLinkedSessionFolder); }
    [Fact] public void Recovery_Requires_Valid_Receipt_And_Collision_Free_Destination() { Assert.Equal(MeetingArchivePreflightStatus.ReceiptUnavailable,MeetingArchivePreflight.Evaluate(Input(MeetingArchiveOperation.Recover,receipt:false)).Status); Assert.Equal(MeetingArchivePreflightStatus.Collision,MeetingArchivePreflight.Evaluate(Input(MeetingArchiveOperation.Recover,collision:true)).Status); }
    [Fact] public void Delete_Requires_Typed_Confirmation_And_Models_Linked_Session_Scope() { var r=MeetingArchivePreflight.Evaluate(Input(MeetingArchiveOperation.PermanentDelete)); Assert.True(r.RequiresTypedDeleteConfirmation); Assert.True(r.IncludesLinkedSessionFolder); }
    private static MeetingArchivePreflightInput Input(MeetingArchiveOperation op,bool receipt=true,bool collision=false)=>new("one",op,[new("audio",true,1),new("transcript",true,2)],false,receipt,true,collision,true);
}
