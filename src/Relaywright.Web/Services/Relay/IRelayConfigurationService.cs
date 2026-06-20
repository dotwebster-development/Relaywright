using Relaywright.Web.Configuration;

namespace Relaywright.Web.Services.Relay;

public interface IRelayConfigurationService
{
    Task<RelayConfigurationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    Task<RelayConfigurationEditModel> GetEditModelAsync(CancellationToken cancellationToken);

    Task SaveAsync(RelayConfigurationEditModel model, CancellationToken cancellationToken);
}

