namespace Relaywright.Web.Services.Runtime;

public sealed class RuntimeStatusSnapshot
{
    public bool IsDeliveryPaused { get; init; }

    public string? DeliveryPauseReason { get; init; }

    public string? DeliveryPausedBy { get; init; }

    public DateTimeOffset? DeliveryPausedUtc { get; init; }

    public RuntimeComponentState SmtpListener { get; init; } = new();

    public RuntimeComponentState DeliveryWorker { get; init; } = new();

    public RuntimeComponentState MaintenanceWorker { get; init; } = new();

    public int ActiveDeliveries { get; init; }

    public int? LastCleanupRemovedRecords { get; init; }

    public DateTimeOffset? LastCleanupUtc { get; init; }
}
