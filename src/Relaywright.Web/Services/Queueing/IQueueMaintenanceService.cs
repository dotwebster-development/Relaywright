using Relaywright.Web.Configuration;

namespace Relaywright.Web.Services.Queueing;

public interface IQueueMaintenanceService
{
    Task<int> CleanupAsync(RelayConfigurationSnapshot configuration, CancellationToken cancellationToken);
}
