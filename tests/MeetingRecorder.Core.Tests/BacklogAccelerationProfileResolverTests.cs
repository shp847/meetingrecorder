using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class BacklogAccelerationProfileResolverTests
{
    [Theory]
    [InlineData(BacklogAccelerationProfile.Normal, false, false)]
    [InlineData(BacklogAccelerationProfile.TranscriptOnlyDrain, false, false)]
    [InlineData(BacklogAccelerationProfile.OvernightAcceleration, true, false)]
    [InlineData(BacklogAccelerationProfile.IdleCapacityAcceleration, false, true)]
    [InlineData(BacklogAccelerationProfile.OvernightAndIdleCapacityAcceleration, true, true)]
    public void Apply_ProjectsOnlyTheRequestedFutureAdmissionPolicy(
        BacklogAccelerationProfile profile,
        bool overnightEnabled,
        bool idleEnabled)
    {
        var projected = BacklogAccelerationProfileResolver.Apply(new AppConfig(), profile);

        Assert.Equal(profile, projected.BacklogAccelerationProfile);
        Assert.Equal(1, projected.BacklogAccelerationProfileMigrationVersion);
        Assert.True(projected.ProcessingScheduleMigrationApplied);
        Assert.Equal(overnightEnabled, BacklogAccelerationProfileResolver.IsOvernightEnabled(projected));
        Assert.Equal(idleEnabled, BacklogAccelerationProfileResolver.IsIdleCapacityEnabled(projected));
        Assert.Equal(
            profile == BacklogAccelerationProfile.TranscriptOnlyDrain
                ? InitialProcessingStrategy.TranscriptFirst
                : InitialProcessingStrategy.ConfiguredStages,
            projected.InitialProcessingStrategy);
    }

    [Fact]
    public void LegacyConfig_PreservesExistingAccelerationUntilItIsMigrated()
    {
        var legacy = new AppConfig
        {
            BacklogAccelerationProfileMigrationVersion = 0,
            BacklogAccelerationProfile = BacklogAccelerationProfile.Normal,
        };

        Assert.True(BacklogAccelerationProfileResolver.IsOvernightEnabled(legacy));
        Assert.True(BacklogAccelerationProfileResolver.IsIdleCapacityEnabled(legacy));
    }

    [Fact]
    public void Options_ExposeOnlyTheFiveDocumentedProfiles()
    {
        Assert.Equal(5, BacklogAccelerationProfileResolver.GetOptions().Count);
    }

    [Fact]
    public async Task ConfigStore_UsesNormalForNewConfigAndPreservesLegacyCombinedBehavior()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var store = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));

        var fresh = await store.LoadOrCreateAsync();
        var migratedLegacy = await store.SaveAsync(new AppConfig());

        Assert.Equal(BacklogAccelerationProfile.Normal, fresh.BacklogAccelerationProfile);
        Assert.Equal(1, fresh.BacklogAccelerationProfileMigrationVersion);
        Assert.Equal(BacklogAccelerationProfile.OvernightAndIdleCapacityAcceleration, migratedLegacy.BacklogAccelerationProfile);
        Assert.Equal(1, migratedLegacy.BacklogAccelerationProfileMigrationVersion);
    }

    [Fact]
    public async Task ConfigStore_PreservesAnExplicitTranscriptOnlyProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var store = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));

        var saved = await store.SaveAsync(BacklogAccelerationProfileResolver.Apply(
            new AppConfig(),
            BacklogAccelerationProfile.TranscriptOnlyDrain));

        Assert.Equal(BacklogAccelerationProfile.TranscriptOnlyDrain, saved.BacklogAccelerationProfile);
        Assert.Equal(InitialProcessingStrategy.TranscriptFirst, saved.InitialProcessingStrategy);
        Assert.Equal(InitialProcessingStrategy.TranscriptFirst, saved.OvernightInitialProcessingStrategy);
    }
}
