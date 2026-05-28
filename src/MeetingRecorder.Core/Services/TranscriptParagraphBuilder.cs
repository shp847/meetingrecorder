using MeetingRecorder.Core.Domain;
using System.Text.RegularExpressions;

namespace MeetingRecorder.Core.Services;

internal sealed record TranscriptParagraph(
    TimeSpan Start,
    TimeSpan End,
    string SpeakerLabel,
    string Text);

internal static partial class TranscriptParagraphBuilder
{
    public static IReadOnlyList<TranscriptParagraph> Build(
        IReadOnlyList<TranscriptSegment> segments,
        Func<TranscriptSegment, string> speakerLabelResolver)
    {
        if (segments.Count == 0)
        {
            return Array.Empty<TranscriptParagraph>();
        }

        var paragraphs = new List<TranscriptParagraph>();
        ParagraphBuilder? current = null;
        foreach (var segment in segments)
        {
            var text = NormalizeSegmentText(segment.Text);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var speakerLabel = NormalizeSpeakerLabel(speakerLabelResolver(segment));
            if (current is null || !StringComparer.Ordinal.Equals(current.SpeakerLabel, speakerLabel))
            {
                if (current is not null)
                {
                    paragraphs.Add(current.Build());
                }

                current = new ParagraphBuilder(segment.Start, segment.End, speakerLabel, text);
                continue;
            }

            current.Append(segment.End, text);
        }

        if (current is not null)
        {
            paragraphs.Add(current.Build());
        }

        return paragraphs;
    }

    private static string NormalizeSpeakerLabel(string speakerLabel)
    {
        return string.IsNullOrWhiteSpace(speakerLabel)
            ? "Speaker"
            : speakerLabel.Trim();
    }

    private static string NormalizeSegmentText(string text)
    {
        return WhitespacePattern().Replace(text.Trim(), " ");
    }

    private static string JoinText(string existingText, string appendedText)
    {
        return appendedText.Length > 0 && IsAttachedPunctuation(appendedText[0])
            ? existingText + appendedText
            : existingText + " " + appendedText;
    }

    private static bool IsAttachedPunctuation(char value)
    {
        return value is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '}';
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();

    private sealed class ParagraphBuilder
    {
        private string _text;

        public ParagraphBuilder(TimeSpan start, TimeSpan end, string speakerLabel, string text)
        {
            Start = start;
            End = end;
            SpeakerLabel = speakerLabel;
            _text = text;
        }

        public TimeSpan Start { get; }

        public TimeSpan End { get; private set; }

        public string SpeakerLabel { get; }

        public void Append(TimeSpan end, string text)
        {
            End = end > End ? end : End;
            _text = JoinText(_text, text);
        }

        public TranscriptParagraph Build()
        {
            return new TranscriptParagraph(Start, End, SpeakerLabel, _text);
        }
    }
}
