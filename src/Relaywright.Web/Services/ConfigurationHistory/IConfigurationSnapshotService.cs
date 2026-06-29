using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.ConfigurationHistory;

public interface IConfigurationSnapshotService
{
    Task CaptureAsync(
        string area,
        string? userName,
        string summary,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConfigurationSnapshot>> GetRecentAsync(
        int count,
        CancellationToken cancellationToken);

    Task<ConfigurationSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task RollbackAsync(Guid id, string? userName, CancellationToken cancellationToken);
}
