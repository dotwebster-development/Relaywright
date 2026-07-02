using System.Net;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Security;

public interface ITrustedNetworkService
{
    Task<bool> IsTrustedAsync(IPAddress? remoteAddress, CancellationToken cancellationToken);

    Task<TrustedNetwork?> FindMatchingAsync(IPAddress? remoteAddress, CancellationToken cancellationToken);

    Task<IReadOnlyList<TrustedNetwork>> GetAllAsync(CancellationToken cancellationToken);

    Task AddOrUpdateAsync(TrustedNetwork network, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
