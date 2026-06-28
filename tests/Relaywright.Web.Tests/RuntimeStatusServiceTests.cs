using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class RuntimeStatusServiceTests
{
    [Fact]
    public async Task PauseAndResumeDeliveryPersistStateAndPulseQueue()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var events = new RecordingOperationalEventService();
        var signal = new RecordingQueueSignal();
        var service = new RuntimeStatusService(
            database.DbContextFactory,
            events,
            signal,
            NullLogger<RuntimeStatusService>.Instance);

        await service.PauseDeliveryAsync("Upstream maintenance", "admin", CancellationToken.None);
        var paused = await service.GetSnapshotAsync(CancellationToken.None);

        Assert.True(paused.IsDeliveryPaused);
        Assert.Equal("Upstream maintenance", paused.DeliveryPauseReason);
        Assert.Equal("admin", paused.DeliveryPausedBy);

        await service.ResumeDeliveryAsync("admin", CancellationToken.None);
        var resumed = await service.GetSnapshotAsync(CancellationToken.None);

        Assert.False(resumed.IsDeliveryPaused);
        Assert.Equal(1, signal.PulseCount);
        Assert.Contains(events.Events, x => x.Message.Contains("paused", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(events.Events, x => x.Message.Contains("resumed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReportedComponentStateAppearsInSnapshot()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var service = new RuntimeStatusService(
            database.DbContextFactory,
            new RecordingOperationalEventService(),
            new RecordingQueueSignal(),
            NullLogger<RuntimeStatusService>.Instance);

        service.ReportSmtpListenerState("Running", "127.0.0.1:25");
        service.ReportDeliveryWorkerState("Running", activeDeliveries: 2, detail: "Delivering.");
        service.ReportMaintenanceWorkerState("Running", removedRecords: 3, detail: "Cleanup completed.");

        var snapshot = await service.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal("Running", snapshot.SmtpListener.Status);
        Assert.Equal("127.0.0.1:25", snapshot.SmtpListener.Detail);
        Assert.Equal(2, snapshot.ActiveDeliveries);
        Assert.Equal(3, snapshot.LastCleanupRemovedRecords);
    }
}
