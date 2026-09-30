using MeetingRecorder.Core.Services;
namespace MeetingRecorder.Core.Tests;
public sealed class MeetingSearchResolverTests
{
 [Fact] public void Search_Requires_All_Tokens_And_Explains_Match_Categories(){var r=MeetingSearchResolver.Search([Doc()],"Kearney Teams");var m=Assert.Single(r.Matches);Assert.Contains("project",m.Categories);Assert.Contains("platform",m.Categories);}
 [Fact] public void Search_Matches_Quoted_Phrase_And_Transcript_Recommendation_Metadata(){var r=MeetingSearchResolver.Search([Doc()],"\"Client update\" published retry");var m=Assert.Single(r.Matches);Assert.Contains("title",m.Categories);Assert.Contains("transcript",m.Categories);Assert.Contains("recommendation",m.Categories);}
 [Fact] public void Search_Reports_Empty_Result_Without_Mutating_Documents(){var r=MeetingSearchResolver.Search([Doc()],"missing");Assert.Empty(r.Matches);Assert.Contains("No meetings",r.Summary);}
 private static MeetingSearchDocument Doc()=>new("one","Client update","Kearney","Teams","Published","2026-09-29","published","retry summary",["Alex"]);
}
