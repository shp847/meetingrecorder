using System.Text.RegularExpressions;
namespace MeetingRecorder.Core.Services;
public sealed record MeetingSearchDocument(string MeetingId,string Title,string? Project,string Platform,string Status,string StartedLocalDate,string TranscriptAvailability,string? RecommendationReason,IReadOnlyList<string> Attendees);
public sealed record MeetingSearchMatch(string MeetingId,IReadOnlyList<string> Categories);
public sealed record MeetingSearchResult(IReadOnlyList<MeetingSearchMatch> Matches,int TotalCount,string Summary);
public static partial class MeetingSearchResolver
{
    [GeneratedRegex("\"([^\"]+)\"|(\\S+)")] private static partial Regex Terms();
    public static MeetingSearchResult Search(IReadOnlyList<MeetingSearchDocument> documents,string? query)
    {
        ArgumentNullException.ThrowIfNull(documents); var terms=Terms().Matches(query??string.Empty).Select(m=>m.Groups[1].Success?m.Groups[1].Value:m.Groups[2].Value).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x.Trim()).ToArray();
        var matches=documents.Select(doc=>Match(doc,terms)).Where(x=>x is not null).Select(x=>x!).ToArray();
        return new(matches,documents.Count,terms.Length==0?$"Showing {matches.Length} meetings.":matches.Length==0?"No meetings match the current search.":$"Showing {matches.Length} of {documents.Count} meetings.");
    }
    private static MeetingSearchMatch? Match(MeetingSearchDocument d,IReadOnlyList<string> terms)
    {
        var fields=new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase) { ["title"]=[d.Title], ["project"]=[d.Project??""], ["platform"]=[d.Platform], ["status"]=[d.Status], ["date"]=[d.StartedLocalDate], ["transcript"]=[d.TranscriptAvailability], ["recommendation"]=[d.RecommendationReason??""], ["attendee"]=d.Attendees.ToArray() };
        var categories=new List<string>(); foreach(var term in terms){var found=fields.Where(f=>f.Value.Any(v=>v.Contains(term,StringComparison.OrdinalIgnoreCase))).Select(f=>f.Key).ToArray();if(found.Length==0)return null;categories.AddRange(found);} return new(d.MeetingId,categories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
