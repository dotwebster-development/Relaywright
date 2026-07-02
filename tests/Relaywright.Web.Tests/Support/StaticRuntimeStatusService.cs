using Relaywright.Web.Services.Runtime;

namespace Relaywright.Web.Tests.Support;

internal sealed class StaticRuntimeStatusService : IRuntimeStatusService
{
    public bool IsPaused { get; set; }

    public int PauseCallCount { get; private set; }

    public int ResumeCallCount { get; private set; }

    public RuntimeStatusSnapshot Snapshot { get; set; } = new();

    public Task<RuntimeStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Snapshot);
    }

    public Task<bool> IsDeliveryPausedAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(IsPaused);
    }

    public Task PauseDeliveryAsync(string? reason, string? userName, CancellationToken cancellationToken)
    {
        PauseCallCount += 1;
        IsPaused = true;
        return Task.CompletedTask;
    }

    public Task ResumeDeliveryAsync(string? userName, CancellationToken cancellationToken)
    {
        ResumeCallCount += 1;
        IsPaused = false;
        return Task.CompletedTask;
    }

    public void ReportSmtpListenerState(string status, string? detail = null, Exception? exception = null)
    {
    }

    public void ReportDeliveryWorkerState(string status, int activeDeliveries, string? detail = null, Exception? exception = null)
    {
    }

    public void ReportMaintenanceWorkerState(string status, int? removedRecords = null, string? detail = null, Exception? exception = null)
    {
    }
}
