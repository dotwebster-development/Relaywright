using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.ConfigurationHistory;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class ConfigurationSnapshotServiceTests
{
    [Fact]
    public async Task RollbackRestoresRelaySettingsAndNotifiesRuntime()
    {
        await using var database = await SqliteTestStore.CreateAsync(seedRelayConfiguration: true);
        using var appData = TempAppData.Create();
        var notifier = new RuntimeConfigurationNotifier();
        var signal = new RecordingQueueSignal();
        var service = new ConfigurationSnapshotService(
            database.DbContextFactory,
            new AdminWebListenerConfigurationService(
                appData.Paths,
                NullLogger<AdminWebListenerConfigurationService>.Instance),
            appData.Paths,
            notifier,
            signal,
            new NoopApplicationRestartService(),
            new RecordingOperationalEventService(),
            NullLogger<ConfigurationSnapshotService>.Instance);

        await service.CaptureAsync(
            ConfigurationSnapshotService.RelayArea,
            "admin",
            "Before change.",
            CancellationToken.None);
        var snapshot = (await service.GetRecentAsync(10, CancellationToken.None)).Single();

        await using (var dbContext = database.CreateDbContext())
        {
            var configuration = await dbContext.RelayConfigurations.SingleAsync();
            configuration.UpstreamHost = "broken.example.test";
            await dbContext.SaveChangesAsync();
        }

        await service.RollbackAsync(snapshot.Id, "admin", CancellationToken.None);

        await using var verifyContext = database.CreateDbContext();
        var restored = await verifyContext.RelayConfigurations.SingleAsync();
        Assert.Equal("smtp.example.test", restored.UpstreamHost);
        Assert.True(notifier.CurrentVersion > 0);
        Assert.Equal(1, signal.PulseCount);
        Assert.Equal(3, await verifyContext.ConfigurationSnapshots.CountAsync());
    }

    private sealed class NoopApplicationRestartService : IApplicationRestartService
    {
        public Task<ApplicationRestartRequestResult> RequestRestartAsync(
            string reason,
            string? userName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ApplicationRestartRequestResult
            {
                RestartScheduled = false,
                RestartSupported = false,
                Message = "Restart not scheduled."
            });
        }

        public Task ClearAppliedRestartIfNeededAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
