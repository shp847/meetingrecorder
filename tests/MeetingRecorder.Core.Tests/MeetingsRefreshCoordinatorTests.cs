using MeetingRecorder.Core.Services;
namespace MeetingRecorder.Core.Tests;
public sealed class MeetingsRefreshCoordinatorTests
{
 [Fact] public void Resolve_Defers_Recording_And_Preserves_Last_Good(){var s=MeetingsRefreshCoordinator.Resolve(Request(),false,true,false,true);Assert.Equal(MeetingsRefreshStateKind.Deferred,s.Kind);Assert.True(s.PreserveLastGood);}
 [Fact] public void Resolve_Offers_Retry_Without_Clearing_Last_Good(){var s=MeetingsRefreshCoordinator.Resolve(null,false,false,true,true);Assert.Equal(MeetingsRefreshStateKind.RetryNeeded,s.Kind);Assert.True(s.CanRetry);Assert.True(s.PreserveLastGood);}
 [Fact] public void Coalesce_Uses_Strongest_Mode_And_Latest_Stable_Selection(){var r=MeetingsRefreshCoordinator.Coalesce(Request(MeetingsRefreshMode.Fast,"one"),Request(MeetingsRefreshMode.Full,"two"));Assert.Equal(MeetingsRefreshMode.Full,r.Mode);Assert.Equal("two",r.SelectedMeetingId);}
 private static MeetingsRefreshRequest Request(MeetingsRefreshMode m=MeetingsRefreshMode.Fast,string id="one")=>new(m,1,id,false,"test");
}
