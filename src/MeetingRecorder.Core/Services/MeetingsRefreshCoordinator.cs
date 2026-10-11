namespace MeetingRecorder.Core.Services;
public enum MeetingsRefreshMode { Fast=0, Full=1 }
public enum MeetingsRefreshStateKind { Current=0, Refreshing=1, Deferred=2, Stale=3, RetryNeeded=4 }
public sealed record MeetingsRefreshRequest(MeetingsRefreshMode Mode,int SourceVersion,string? SelectedMeetingId,bool IsManual,string Reason);
public sealed record MeetingsRefreshState(MeetingsRefreshStateKind Kind,MeetingsRefreshRequest? Pending,string Status,bool PreserveLastGood,bool CanRetry);
public static class MeetingsRefreshCoordinator
{
 public static MeetingsRefreshState Resolve(MeetingsRefreshRequest? pending,bool isRunning,bool isRecording,bool hasFailure,bool hasLastGood)=>
  isRecording&&pending is not null?new(MeetingsRefreshStateKind.Deferred,pending,"Refresh deferred while recording; current meetings stay available.",true,false):
  isRunning?new(MeetingsRefreshStateKind.Refreshing,pending,"Refreshing meeting status.",true,false):
  hasFailure?new(MeetingsRefreshStateKind.RetryNeeded,pending,"Meeting refresh needs retry; last successful meetings remain available.",hasLastGood,true):
  pending is not null?new(MeetingsRefreshStateKind.Stale,pending,"Meeting status needs refresh.",hasLastGood,false):new(MeetingsRefreshStateKind.Current,null,"Meeting status is current.",hasLastGood,false);
 public static MeetingsRefreshRequest Coalesce(MeetingsRefreshRequest current,MeetingsRefreshRequest incoming)=>incoming with { Mode=(MeetingsRefreshMode)Math.Max((int)current.Mode,(int)incoming.Mode), SelectedMeetingId=incoming.SelectedMeetingId??current.SelectedMeetingId };
}
