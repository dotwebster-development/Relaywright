using Relaywright.Web.Configuration;
using Relaywright.Web.Services.Backups;
using Relaywright.Web.Services.Runtime;

namespace Relaywright.Web.Tests.Support;

internal sealed class StaticDashboardReadinessService : IDashboardReadinessService
{
    public DashboardReadinessSnapshot Snapshot { get; set; } = DashboardReadinessSnapshot.Empty;

    public Task<DashboardReadinessSnapshot> GetSnapshotAsync(
        RelayConfigurationSnapshot configuration,
        BackupReadiness backupReadiness,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Snapshot);
    }
}
