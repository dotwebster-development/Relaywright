using Relaywright.Web.Configuration;

namespace Relaywright.Web.Services.Runtime;

public interface IDashboardMetricsService
{
    Task<DashboardMetricsSnapshot> GetSnapshotAsync(
        RelayConfigurationSnapshot configuration,
        CancellationToken cancellationToken);
}
