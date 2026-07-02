using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Services.ConfigurationHistory;

public sealed class ConfigurationSnapshotService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IAdminWebListenerConfigurationService adminWebListenerConfigurationService,
    AppPaths appPaths,
    IRuntimeConfigurationNotifier runtimeConfigurationNotifier,
    IQueueSignal queueSignal,
    IApplicationRestartService applicationRestartService,
    IOperationalEventService eventService,
    ILogger<ConfigurationSnapshotService> logger) : IConfigurationSnapshotService
{
    public const string RelayArea = "Relay";
    public const string SubmissionPolicyArea = "SubmissionPolicy";
    public const string TrustedNetworksArea = "TrustedNetworks";
    public const string AlertRulesArea = "AlertRules";
    public const string BackupScheduleArea = "BackupSchedule";
    public const string AdminWebListenerArea = "AdminWebListener";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task CaptureAsync(
        string area,
        string? userName,
        string summary,
        CancellationToken cancellationToken)
    {
        var (displayName, payload) = await CreatePayloadAsync(area, cancellationToken);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.ConfigurationSnapshots.Add(new ConfigurationSnapshot
        {
            Id = Guid.NewGuid(),
            Area = area,
            DisplayName = displayName,
            Summary = Trim(summary, 2048) ?? string.Empty,
            PayloadJson = payload,
            CreatedBy = Trim(userName, 256),
            CreatedUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Captured configuration snapshot. Area={Area}; DisplayName={DisplayName}; User={UserName}",
            area,
            displayName,
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

        switch (snapshot.Area)
        {
            case RelayArea:
                await RestoreRelayAsync(snapshot.PayloadJson, cancellationToken);
                break;
            case SubmissionPolicyArea:
                await RestoreSubmissionPolicyAsync(snapshot.PayloadJson, cancellationToken);
                break;
            case TrustedNetworksArea:
                await RestoreTrustedNetworksAsync(snapshot.PayloadJson, cancellationToken);
                break;
            case AlertRulesArea:
                await RestoreAlertRulesAsync(snapshot.PayloadJson, cancellationToken);
                break;
            case BackupScheduleArea:
                await RestoreBackupScheduleAsync(snapshot.PayloadJson, cancellationToken);
                break;
            case AdminWebListenerArea:
                await RestoreAdminWebListenerAsync(snapshot.PayloadJson, userName, cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Unsupported configuration snapshot area '{snapshot.Area}'.");
        }

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

    private async Task<(string DisplayName, string Payload)> CreatePayloadAsync(
        string area,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return area switch
        {
            RelayArea => ("Relay Settings", JsonSerializer.Serialize(
                await dbContext.RelayConfigurations.AsNoTracking().SingleAsync(cancellationToken),
                JsonOptions)),
            SubmissionPolicyArea => ("Submission Policy", JsonSerializer.Serialize(
                await dbContext.SubmissionPolicies.AsNoTracking().SingleAsync(x => x.Id == 1, cancellationToken),
                JsonOptions)),
            TrustedNetworksArea => ("Trusted IPs", JsonSerializer.Serialize(
                await dbContext.TrustedNetworks.AsNoTracking().OrderBy(x => x.Cidr).ToListAsync(cancellationToken),
                JsonOptions)),
            AlertRulesArea => ("Alerts", JsonSerializer.Serialize(
                await dbContext.AlertRules.AsNoTracking().OrderBy(x => x.Key).ToListAsync(cancellationToken),
                JsonOptions)),
            BackupScheduleArea => ("Backup Schedule", JsonSerializer.Serialize(
                await dbContext.BackupScheduleStates.AsNoTracking().SingleAsync(x => x.Id == 1, cancellationToken),
                JsonOptions)),
            AdminWebListenerArea => ("Web Interface", JsonSerializer.Serialize(
                new AdminWebListenerSnapshot(await adminWebListenerConfigurationService.GetConfigurationAsync(cancellationToken)),
                JsonOptions)),
            _ => throw new InvalidOperationException($"Unsupported configuration snapshot area '{area}'.")
        };
    }

    private async Task RestoreRelayAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = Deserialize<RelayConfiguration>(payloadJson);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.RelayConfigurations.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (existing is null)
        {
            payload.Id = 1;
            dbContext.RelayConfigurations.Add(payload);
        }
        else
        {
            dbContext.Entry(existing).CurrentValues.SetValues(payload);
            existing.Id = 1;
            existing.UpdatedUtc = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        runtimeConfigurationNotifier.NotifySmtpSettingsChanged();
        queueSignal.Pulse();
    }

    private async Task RestoreSubmissionPolicyAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = Deserialize<SubmissionPolicy>(payloadJson);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.SubmissionPolicies.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (existing is null)
        {
            payload.Id = 1;
            dbContext.SubmissionPolicies.Add(payload);
        }
        else
        {
            dbContext.Entry(existing).CurrentValues.SetValues(payload);
            existing.Id = 1;
            existing.UpdatedUtc = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RestoreTrustedNetworksAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = Deserialize<List<TrustedNetwork>>(payloadJson);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.TrustedNetworks.RemoveRange(dbContext.TrustedNetworks);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.TrustedNetworks.AddRange(payload);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RestoreAlertRulesAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = Deserialize<List<AlertRule>>(payloadJson);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existingRules = await dbContext.AlertRules.ToListAsync(cancellationToken);
        foreach (var savedRule in payload)
        {
            var existing = existingRules.FirstOrDefault(x => string.Equals(x.Key, savedRule.Key, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                savedRule.Id = 0;
                savedRule.Results.Clear();
                dbContext.AlertRules.Add(savedRule);
                continue;
            }

            existing.DisplayName = savedRule.DisplayName;
            existing.Description = savedRule.Description;
            existing.IsEnabled = savedRule.IsEnabled;
            existing.Threshold = savedRule.Threshold;
            existing.CooldownMinutes = savedRule.CooldownMinutes;
            existing.EmailRecipients = savedRule.EmailRecipients;
            existing.IsActive = savedRule.IsActive;
            existing.LastTriggeredUtc = savedRule.LastTriggeredUtc;
            existing.LastResolvedUtc = savedRule.LastResolvedUtc;
            existing.LastNotificationUtc = savedRule.LastNotificationUtc;
            existing.LastNotificationSucceeded = savedRule.LastNotificationSucceeded;
            existing.LastNotificationMessage = savedRule.LastNotificationMessage;
            existing.UpdatedUtc = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RestoreBackupScheduleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = Deserialize<BackupScheduleState>(payloadJson);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.BackupScheduleStates.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (existing is null)
        {
            payload.Id = 1;
            dbContext.BackupScheduleStates.Add(payload);
        }
        else
        {
            dbContext.Entry(existing).CurrentValues.SetValues(payload);
            existing.Id = 1;
            existing.UpdatedUtc = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RestoreAdminWebListenerAsync(
        string payloadJson,
        string? userName,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<AdminWebListenerSnapshot>(payloadJson);
        if (payload.Configuration is null)
        {
            if (File.Exists(appPaths.AdminWebListenerConfigurationPath))
            {
                File.Delete(appPaths.AdminWebListenerConfigurationPath);
            }
        }
        else
        {
            await adminWebListenerConfigurationService.SaveAsync(payload.Configuration, cancellationToken);
        }

        await applicationRestartService.RequestRestartAsync(
            "Admin web listener settings were rolled back.",
            userName,
            cancellationToken);
    }

    private static T Deserialize<T>(string payloadJson)
    {
        return JsonSerializer.Deserialize<T>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Configuration snapshot payload could not be read.");
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

    private sealed class AdminWebListenerSnapshot(AdminWebListenerConfiguration? configuration)
    {
        public AdminWebListenerConfiguration? Configuration { get; init; } = configuration;
    }
}
