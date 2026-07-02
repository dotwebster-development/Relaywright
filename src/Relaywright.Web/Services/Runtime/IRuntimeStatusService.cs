namespace Relaywright.Web.Services.Runtime;

public interface IRuntimeStatusService
{
    Task<RuntimeStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    Task<bool> IsDeliveryPausedAsync(CancellationToken cancellationToken);

    Task PauseDeliveryAsync(string? reason, string? userName, CancellationToken cancellationToken);

    Task ResumeDeliveryAsync(string? userName, CancellationToken cancellationToken);

    void ReportSmtpListenerState(string status, string? detail = null, Exception? exception = null);

    void ReportDeliveryWorkerState(string status, int activeDeliveries, string? detail = null, Exception? exception = null);

    void ReportMaintenanceWorkerState(string status, int? removedRecords = null, string? detail = null, Exception? exception = null);
}
