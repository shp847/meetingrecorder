using System.Text;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

internal sealed record TranscriptSpeakerBlockPart(MeetingTranscriptSegmentRow Row, int Start, int Length);

internal sealed record TranscriptSpeakerBlock(
    MeetingTranscriptSegmentRow First,
    string Text,
    IReadOnlyList<TranscriptSpeakerBlockPart> Parts)
{
    public MeetingTranscriptSegmentRow AsRow() => First with
    {
        Text = Text,
        BlockParts = Parts,
        InlineRenameSpeakerId = First.EffectiveSpeakerKey?.StartsWith("speaker:", StringComparison.Ordinal) == true
            ? First.EffectiveSpeakerKey["speaker:".Length..] : null,
    };
}

internal static class TranscriptSpeakerBlockBuilder
{
    public static IReadOnlyList<TranscriptSpeakerBlock> Build(IReadOnlyList<MeetingTranscriptSegmentRow> rows)
    {
        var blocks = new List<TranscriptSpeakerBlock>();
        var parts = new List<TranscriptSpeakerBlockPart>();
        var text = new StringBuilder();
        MeetingTranscriptSegmentRow? previous = null;

        void Finish()
        {
            if (parts.Count == 0) return;
            blocks.Add(new TranscriptSpeakerBlock(parts[0].Row, text.ToString(), parts.ToArray()));
            parts.Clear();
            text.Clear();
        }

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Text)) { Finish(); previous = null; continue; }
            var mayJoin = previous is not null && previous.AttributionState != EffectiveSegmentAttributionState.ReviewNeeded &&
                row.AttributionState != EffectiveSegmentAttributionState.ReviewNeeded &&
                !string.IsNullOrWhiteSpace(previous.EffectiveSpeakerKey) &&
                string.Equals(previous.EffectiveSpeakerKey, row.EffectiveSpeakerKey, StringComparison.Ordinal);
            if (!mayJoin) Finish();
            if (text.Length > 0 && !char.IsWhiteSpace(text[^1]) && !char.IsWhiteSpace(row.Text[0]) &&
                !IsAttachedPunctuation(row.Text[0]) &&
                (string.IsNullOrWhiteSpace(row.ManualSplitId) || row.ManualSplitId != previous?.ManualSplitId) &&
                (string.IsNullOrWhiteSpace(row.SourceSegmentId) || row.SourceSegmentId != previous?.SourceSegmentId))
                text.Append(' ');
            var start = text.Length;
            text.Append(row.Text);
            parts.Add(new TranscriptSpeakerBlockPart(row, start, row.Text.Length));
            previous = row;
        }

        Finish();
        return blocks;
    }

    private static bool IsAttachedPunctuation(char value) => value is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '}';

    public static IReadOnlyList<TranscriptSelectionSlice> GetSelection(MeetingTranscriptSegmentRow block, int start, int length)
    {
        if (start < 0 || length <= 0 || start > block.Text.Length - length) return [];
        var end = start + length;
        var parts = block.BlockParts ?? [new TranscriptSpeakerBlockPart(block, 0, block.Text.Length)];
        var slices = new List<TranscriptSelectionSlice>();
        foreach (var part in parts)
        {
            var left = Math.Max(start, part.Start) - part.Start;
            var right = Math.Min(end, part.Start + part.Length) - part.Start;
            if (right <= left || string.IsNullOrWhiteSpace(part.Row.Text[left..right])) continue;
            if (string.IsNullOrWhiteSpace(part.Row.Text[..left])) left = 0;
            if (string.IsNullOrWhiteSpace(part.Row.Text[right..])) right = part.Length;
            if (string.IsNullOrWhiteSpace(part.Row.SegmentId)) return [];
            slices.Add(new TranscriptSelectionSlice(part.Row.SegmentId, part.Row.SpeakerId, part.Row.Text, left, right - left));
        }
        return slices;
    }
}
