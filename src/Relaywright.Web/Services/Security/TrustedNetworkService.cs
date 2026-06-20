using System.Net;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.Security;

public sealed class TrustedNetworkService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IOperationalEventService eventService,
    ILogger<TrustedNetworkService> logger) : ITrustedNetworkService
{
    public async Task<bool> IsTrustedAsync(IPAddress? remoteAddress, CancellationToken cancellationToken)
    {
        if (remoteAddress is null)
        {
            logger.LogDebug("Trusted network check failed because remote address was unavailable.");
            return false;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var networks = await dbContext.TrustedNetworks
            .AsNoTracking()
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Cidr)
            .ToListAsync(cancellationToken);

        foreach (var network in networks)
        {
            if (CidrRange.TryParse(network.Cidr, out var range) && range!.Contains(remoteAddress))
            {
                logger.LogDebug(
                    "Remote address matched trusted network. RemoteIp={RemoteIp}; Cidr={Cidr}; Description={Description}",
                    remoteAddress,
                    network.Cidr,
                    network.Description);
                return true;
            }
        }

        logger.LogWarning(
            "Remote address did not match any enabled trusted network. RemoteIp={RemoteIp}; EnabledNetworkCount={EnabledNetworkCount}",
            remoteAddress,
            networks.Count);

        return false;
    }

    public async Task<IReadOnlyList<TrustedNetwork>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.TrustedNetworks
            .AsNoTracking()
            .OrderBy(x => x.Cidr)
            .ToListAsync(cancellationToken);
    }

    public async Task AddOrUpdateAsync(TrustedNetwork network, CancellationToken cancellationToken)
    {
        var cidr = network.Cidr.Trim();
        var description = network.Description.Trim();

        if (!CidrRange.TryParse(cidr, out _))
        {
            logger.LogWarning("Trusted network save rejected because CIDR/IP was invalid. Cidr={Cidr}", cidr);
            throw new InvalidOperationException("The trusted network must be a valid IP address or CIDR range.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = network.Id == 0
            ? null
            : await dbContext.TrustedNetworks.SingleOrDefaultAsync(x => x.Id == network.Id, cancellationToken);
        var created = existing is null;
        TrustedNetwork savedNetwork;

        if (existing is null)
        {
            savedNetwork = new TrustedNetwork
            {
                Cidr = cidr,
                Description = description,
                IsEnabled = network.IsEnabled,
                CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow
            };

            dbContext.TrustedNetworks.Add(savedNetwork);
        }
        else
        {
            existing.Cidr = cidr;
            existing.Description = description;
            existing.IsEnabled = network.IsEnabled;
            existing.UpdatedUtc = DateTimeOffset.UtcNow;
            savedNetwork = existing;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Trusted network {Action}. Id={Id}; Cidr={Cidr}; Enabled={Enabled}; Description={Description}",
            created ? "created" : "updated",
            savedNetwork.Id,
            cidr,
            network.IsEnabled,
            description);

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.Configuration,
            Message = $"Trusted network {(created ? "created" : "updated")}: {cidr}"
        }, cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.TrustedNetworks.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            logger.LogWarning("Trusted network delete skipped because record was not found. Id={TrustedNetworkId}", id);
            return;
        }

        dbContext.TrustedNetworks.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Trusted network deleted. Id={TrustedNetworkId}; Cidr={Cidr}; Enabled={Enabled}",
            id,
            existing.Cidr,
            existing.IsEnabled);

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.Configuration,
            Message = $"Trusted network deleted: {existing.Cidr}"
        }, cancellationToken);
    }
}
