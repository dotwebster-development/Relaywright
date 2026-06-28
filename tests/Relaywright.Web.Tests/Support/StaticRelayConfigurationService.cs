using Relaywright.Web.Configuration;
using Relaywright.Web.Services.Relay;

namespace Relaywright.Web.Tests.Support;

internal sealed class StaticRelayConfigurationService(RelayConfigurationSnapshot? snapshot = null) : IRelayConfigurationService
{
    public RelayConfigurationSnapshot Snapshot { get; set; } = snapshot ?? TestData.Snapshot();

    public RelayConfigurationEditModel EditModel { get; set; } = new();

    public List<RelayConfigurationEditModel> SavedModels { get; } = new();

    public Task<RelayConfigurationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Snapshot);
    }

    public Task<RelayConfigurationEditModel> GetEditModelAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(EditModel);
    }

    public Task SaveAsync(RelayConfigurationEditModel model, CancellationToken cancellationToken)
    {
        SavedModels.Add(model);
        return Task.CompletedTask;
    }
}
