using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.ConfigurationHistory;

public sealed class ConfigurationSnapshotService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ConfigurationSnapshotPayloadFactory payloadFactory,
    ConfigurationSnapshotRestorer restorer,
    IOperationalEventService eventService,
    ILogger<ConfigurationSnapshotService> logger) : IConfigurationSnapshotService
{
    public const string RelayArea = ConfigurationSnapshotAreas.Relay;
    public const string SubmissionPolicyArea = ConfigurationSnapshotAreas.SubmissionPolicy;
    public const string TrustedNetworksArea = ConfigurationSnapshotAreas.TrustedNetworks;
    public const string AlertRulesArea = ConfigurationSnapshotAreas.AlertRules;
    public const string BackupScheduleArea = ConfigurationSnapshotAreas.BackupSchedule;
    public const string AdminWebListenerArea = ConfigurationSnapshotAreas.AdminWebListener;

    public async Task CaptureAsync(
        string area,
        string? userName,
        string summary,
        CancellationToken cancellationToken)
    {
        var payload = await payloadFactory.CreateAsync(area, cancellationToken);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.ConfigurationSnapshots.Add(new ConfigurationSnapshot
        {
            Id = Guid.NewGuid(),
            Area = area,
            DisplayName = payload.DisplayName,
            Summary = Trim(summary, 2048) ?? string.Empty,
            PayloadJson = payload.PayloadJson,
            CreatedBy = Trim(userName, 256),
            CreatedUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Captured configuration snapshot. Area={Area}; DisplayName={DisplayName}; User={UserName}",
            area,
            payload.DisplayName,
            userName);
    }

    public async Task<IReadOnlyList<ConfigurationSnapshot>> GetRecentAsync(
        int count,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var snapshots = await dbContext.ConfigurationSnapshots
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return snapshots
            .OrderByDescending(x => x.CreatedUtc)
            .Take(Math.Max(1, count))
            .ToList();
    }

    public async Task<ConfigurationSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.ConfigurationSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task RollbackAsync(Guid id, string? userName, CancellationToken cancellationToken)
    {
        var snapshot = await GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Configuration snapshot not found.");

        await CaptureAsync(
            snapshot.Area,
            userName,
            $"Automatic safety snapshot before rollback to {snapshot.CreatedUtc:O}.",
            cancellationToken);

        await restorer.RestoreAsync(
            snapshot.Area,
            snapshot.PayloadJson,
            userName,
            cancellationToken);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.ConfigurationSnapshots.Add(new ConfigurationSnapshot
        {
            Id = Guid.NewGuid(),
            Area = snapshot.Area,
            DisplayName = snapshot.DisplayName,
            Summary = $"Rollback applied from snapshot created {snapshot.CreatedUtc:O}.",
            PayloadJson = snapshot.PayloadJson,
            CreatedBy = Trim(userName, 256),
            CreatedUtc = DateTimeOffset.UtcNow,
            IsRollback = true
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.Configuration,
            Message = $"Configuration rollback applied: {snapshot.DisplayName}.",
            Detail = snapshot.Summary
        }, cancellationToken);

        logger.LogInformation(
            "Configuration rollback applied. SnapshotId={SnapshotId}; Area={Area}; User={UserName}",
            snapshot.Id,
            snapshot.Area,
            userName);
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
