using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Domain;

public sealed record MeetingSummary(
    string Overview,
    IReadOnlyList<string> KeyPoints,
    IReadOnlyList<string> Decisions,
    IReadOnlyList<MeetingSummaryActionItem> ActionItems,
    IReadOnlyList<string> RisksAndOpenQuestions,
    MeetingSummaryProviderInfo Provider,
    DateTimeOffset GeneratedAtUtc,
    string TranscriptFingerprint);

public sealed record MeetingSummaryActionItem(
    string Text,
    string? Owner,
    string? DueDateText);

public sealed record MeetingSummaryProviderInfo(
    SummaryChatProviderKind ProviderKind,
    string ProviderName,
    string Model,
    bool FallbackUsed,
    ModelProxyRoutingInfo? ModelProxyRouting = null);
