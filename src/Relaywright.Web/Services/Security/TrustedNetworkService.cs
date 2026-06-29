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
        return await FindMatchingAsync(remoteAddress, cancellationToken) is not null;
    }

    public async Task<TrustedNetwork?> FindMatchingAsync(IPAddress? remoteAddress, CancellationToken cancellationToken)
    {
        if (remoteAddress is null)
        {
            logger.LogDebug("Trusted network check failed because remote address was unavailable.");
            return null;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var networks = await dbContext.TrustedNetworks
            .AsNoTracking()
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Cidr)
            .ToListAsync(cancellationToken);

        TrustedNetwork? matchingNetwork = null;
        var matchingPrefixLength = -1;
        foreach (var network in networks)
        {
            if (CidrRange.TryParse(network.Cidr, out var range) && range!.Contains(remoteAddress))
            {
                if (range.PrefixLength > matchingPrefixLength)
                {
                    matchingNetwork = network;
                    matchingPrefixLength = range.PrefixLength;
                }
            }
        }

        if (matchingNetwork is not null)
        {
            logger.LogDebug(
                "Remote address matched trusted network. RemoteIp={RemoteIp}; Cidr={Cidr}; Description={Description}",
                remoteAddress,
                matchingNetwork.Cidr,
                matchingNetwork.Description);
            return matchingNetwork;
        }

        logger.LogWarning(
            "Remote address did not match any enabled trusted network. RemoteIp={RemoteIp}; EnabledNetworkCount={EnabledNetworkCount}",
            remoteAddress,
            networks.Count);

        return null;
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
        var owner = NormalizeOptional(network.Owner);
        var location = NormalizeOptional(network.Location);

        if (!CidrRange.TryParse(cidr, out var range))
        {
            logger.LogWarning("Trusted network save rejected because CIDR/IP was invalid. Cidr={Cidr}", cidr);
            throw new InvalidOperationException("The trusted network must be a valid IP address or CIDR range.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var otherNetworks = await dbContext.TrustedNetworks
            .AsNoTracking()
            .Where(x => x.Id != network.Id)
            .ToListAsync(cancellationToken);
        foreach (var otherNetwork in otherNetworks)
        {
            if (CidrRange.TryParse(otherNetwork.Cidr, out var otherRange) && range!.Overlaps(otherRange!))
            {
                logger.LogWarning(
                    "Trusted network save rejected because CIDR overlaps an existing trusted network. Cidr={Cidr}; ExistingId={ExistingId}; ExistingCidr={ExistingCidr}",
                    cidr,
                    otherNetwork.Id,
                    otherNetwork.Cidr);
                throw new InvalidOperationException($"Trusted network overlaps with existing entry '{otherNetwork.Cidr}'.");
            }
        }

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
                Owner = owner,
                Location = location,
                AllowedSenderAddresses = NormalizePolicyList(network.AllowedSenderAddresses),
                BlockedSenderAddresses = NormalizePolicyList(network.BlockedSenderAddresses),
                AllowedRecipientDomains = NormalizePolicyList(network.AllowedRecipientDomains),
                BlockedRecipientDomains = NormalizePolicyList(network.BlockedRecipientDomains),
                MaxMessageSizeBytes = NormalizePositive(network.MaxMessageSizeBytes),
                MaxRecipientsPerMessage = NormalizePositive(network.MaxRecipientsPerMessage),
                RateLimitMessagesPerHour = NormalizePositive(network.RateLimitMessagesPerHour),
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
            existing.Owner = owner;
            existing.Location = location;
            existing.AllowedSenderAddresses = NormalizePolicyList(network.AllowedSenderAddresses);
            existing.BlockedSenderAddresses = NormalizePolicyList(network.BlockedSenderAddresses);
            existing.AllowedRecipientDomains = NormalizePolicyList(network.AllowedRecipientDomains);
            existing.BlockedRecipientDomains = NormalizePolicyList(network.BlockedRecipientDomains);
            existing.MaxMessageSizeBytes = NormalizePositive(network.MaxMessageSizeBytes);
            existing.MaxRecipientsPerMessage = NormalizePositive(network.MaxRecipientsPerMessage);
            existing.RateLimitMessagesPerHour = NormalizePositive(network.RateLimitMessagesPerHour);
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

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizePolicyList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var entries = value
            .Split([',', ';', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return entries.Length == 0 ? null : string.Join(Environment.NewLine, entries);
    }

    private static long? NormalizePositive(long? value) => value is > 0 ? value : null;

    private static int? NormalizePositive(int? value) => value is > 0 ? value : null;
}
