using Relaywright.Web.Configuration;
using Relaywright.Web.Services.Backups;

namespace Relaywright.Web.Services.Runtime;

public interface IDashboardReadinessService
{
    Task<DashboardReadinessSnapshot> GetSnapshotAsync(
        RelayConfigurationSnapshot configuration,
        BackupReadiness backupReadiness,
        CancellationToken cancellationToken);
}
