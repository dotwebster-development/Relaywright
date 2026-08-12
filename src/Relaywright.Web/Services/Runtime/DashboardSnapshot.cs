using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Services.Updates;

namespace Relaywright.Web.Services.Runtime;

public sealed class DashboardSnapshot
{
    public RelayConfigurationSnapshot Configuration { get; init; } = new();

    public RuntimeStatusSnapshot RuntimeStatus { get; init; } = new();

    public DashboardMetricsSnapshot Metrics { get; init; } = new();

    public DashboardReadinessSnapshot Readiness { get; init; } = DashboardReadinessSnapshot.Empty;

    public SuspiciousLoginSummary SuspiciousLogins { get; init; } = SuspiciousLoginSummary.Empty;

    public UpdateCheckStatus UpdateStatus { get; init; } = UpdateCheckStatus.Initial();

    public int PendingCount { get; init; }

    public int RetryCount { get; init; }

    public int FailedCount { get; init; }

    public int DeliveredTodayCount { get; init; }

    public IReadOnlyList<OperationalEvent> RecentEvents { get; init; } = [];
}
