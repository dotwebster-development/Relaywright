using Relaywright.Web.Services.Backups;

namespace Relaywright.Web.Services.Runtime;

public sealed class DashboardMetricsSnapshot
{
    public int AcceptedLast24Hours { get; init; }

    public int DeliveredLast24Hours { get; init; }

    public int FailedLast24Hours { get; init; }

    public int RetryingCount { get; init; }

    public long? OldestActiveAgeMinutes { get; init; }

    public DateTimeOffset? LastUpstreamFailureUtc { get; init; }

    public string? LastUpstreamFailureMessage { get; init; }

    public long DatabaseSizeBytes { get; init; }

    public bool IsDatabaseExternallyManaged { get; init; }

    public string DatabaseDescription { get; init; } = string.Empty;

    public long SpoolSizeBytes { get; init; }

    public long BackupSizeBytes { get; init; }

    public OutboundRouteResult OutboundRoute { get; init; } = new();

    public BackupReadiness BackupReadiness { get; init; } = new();
}
