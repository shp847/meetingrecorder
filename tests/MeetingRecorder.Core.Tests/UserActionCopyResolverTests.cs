using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class UserActionCopyResolverTests
{
    [Fact]
    public void Resolve_Covers_Every_Intent_With_Outcome_And_Progress_Copy()
    {
        foreach (var intent in Enum.GetValues<UserActionIntent>())
        {
            var copy = UserActionCopyResolver.Resolve(intent);

            Assert.True(copy.IsAvailable);
            Assert.False(string.IsNullOrWhiteSpace(copy.Label));
            Assert.False(string.IsNullOrWhiteSpace(copy.HelperText));
            Assert.False(string.IsNullOrWhiteSpace(copy.ProgressText));
            Assert.False(string.IsNullOrWhiteSpace(copy.SuccessText));
            Assert.False(string.IsNullOrWhiteSpace(copy.LiveAnnouncement));
        }
    }

    [Fact]
    public void Resolve_Covers_Every_Blocked_Reason_With_Safe_Recovery_Copy()
    {
        foreach (var kind in Enum.GetValues<UserActionBlockedReasonKind>())
        {
            var reason = UserActionCopyResolver.ResolveBlockedReason(kind);
            var copy = UserActionCopyResolver.Resolve(UserActionIntent.GenerateSummary, kind);

            if (kind == UserActionBlockedReasonKind.None)
            {
                Assert.True(copy.IsAvailable);
                continue;
            }

            Assert.False(copy.IsAvailable);
            Assert.False(string.IsNullOrWhiteSpace(reason.Condition));
            Assert.False(string.IsNullOrWhiteSpace(reason.SafetyConstraint));
            Assert.NotEqual(UserActionRemedyDestination.None, reason.RemedyDestination);
            Assert.False(string.IsNullOrWhiteSpace(copy.BlockedText));
            Assert.False(string.IsNullOrWhiteSpace(copy.PrimaryActionText));

            AssertNormalCopyHasNoDiagnostics(reason.Condition);
            AssertNormalCopyHasNoDiagnostics(reason.SafetyConstraint);
            AssertNormalCopyHasNoDiagnostics(reason.HelpText ?? string.Empty);
            AssertNormalCopyHasNoDiagnostics(copy.BlockedText);
        }
    }

    [Fact]
    public void ResolveBlockedReason_Covers_Every_User_Surface()
    {
        var coveredScopes = Enum.GetValues<UserActionBlockedReasonKind>()
            .Select(UserActionCopyResolver.ResolveBlockedReason)
            .Select(reason => reason.Scope)
            .ToHashSet();

        foreach (var scope in Enum.GetValues<UserActionScope>())
        {
            Assert.Contains(scope, coveredScopes);
        }
    }

    [Fact]
    public void Resolve_Maps_Hosted_Summary_Consent_To_Settings_Without_Routing_Detail()
    {
        var reason = UserActionCopyResolver.ResolveBlockedReason(
            UserActionBlockedReasonKind.HostedSummaryConsentRequired);
        var copy = UserActionCopyResolver.Resolve(
            UserActionIntent.GenerateSummary,
            UserActionBlockedReasonKind.HostedSummaryConsentRequired);

        Assert.Equal(UserActionScope.Settings, reason.Scope);
        Assert.Equal(UserActionRemedyDestination.SummarySettings, reason.RemedyDestination);
        Assert.Equal("Open Summary Settings", copy.PrimaryActionText);
        Assert.Contains("No transcript is sent", reason.SafetyConstraint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_Durable_Actions_Describe_Their_Confirmation_Scope()
    {
        var intents = new[]
        {
            UserActionIntent.UndoProfileNames,
            UserActionIntent.DisableVoiceProfile,
            UserActionIntent.DeleteVoiceProfile,
            UserActionIntent.DeleteAllVoiceProfiles,
            UserActionIntent.ArchiveMeetings,
            UserActionIntent.DeleteMeetings,
            UserActionIntent.DismissCleanupRecommendations,
        };

        foreach (var intent in intents)
        {
            var copy = UserActionCopyResolver.Resolve(intent);

            Assert.False(string.IsNullOrWhiteSpace(copy.ConfirmationText));
            AssertNormalCopyHasNoDiagnostics(copy.ConfirmationText!);
        }
    }

    private static void AssertNormalCopyHasNoDiagnostics(string text)
    {
        Assert.DoesNotContain("C:\\", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("openai", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("modelproxy", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gpt-", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("embedding", text, StringComparison.OrdinalIgnoreCase);
    }
}
