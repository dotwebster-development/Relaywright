using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Services.ConfigurationHistory;

public sealed class ConfigurationSnapshotRestorer(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IAdminWebListenerConfigurationService adminWebListenerConfigurationService,
    AppPaths appPaths,
    IRuntimeConfigurationNotifier runtimeConfigurationNotifier,
    IQueueSignal queueSignal,
    IApplicationRestartService applicationRestartService,
    ConfigurationSnapshotSerializer serializer)
{
    public Task RestoreAsync(
        string area,
        string payloadJson,
        string? userName,
        CancellationToken cancellationToken)
    {
        return area switch
        {
            ConfigurationSnapshotAreas.Relay => RestoreRelayAsync(payloadJson, cancellationToken),
            ConfigurationSnapshotAreas.SubmissionPolicy => RestoreSubmissionPolicyAsync(payloadJson, cancellationToken),
            ConfigurationSnapshotAreas.TrustedNetworks => RestoreTrustedNetworksAsync(payloadJson, cancellationToken),
            ConfigurationSnapshotAreas.AlertRules => RestoreAlertRulesAsync(payloadJson, cancellationToken),
            ConfigurationSnapshotAreas.BackupSchedule => RestoreBackupScheduleAsync(payloadJson, cancellationToken),
            ConfigurationSnapshotAreas.AdminWebListener => RestoreAdminWebListenerAsync(
                payloadJson,
                userName,
                cancellationToken),
            _ => throw new InvalidOperationException(
                $"Unsupported configuration snapshot area '{area}'.")
        };
    }

    private async Task RestoreRelayAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = serializer.Deserialize<RelayConfiguration>(payloadJson);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.RelayConfigurations.SingleOrDefaultAsync(
            configuration => configuration.Id == 1,
            cancellationToken);
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
        var payload = serializer.Deserialize<SubmissionPolicy>(payloadJson);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.SubmissionPolicies.SingleOrDefaultAsync(
            policy => policy.Id == 1,
            cancellationToken);
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
        var payload = serializer.Deserialize<List<TrustedNetwork>>(payloadJson);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.TrustedNetworks.RemoveRange(dbContext.TrustedNetworks);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.TrustedNetworks.AddRange(payload);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RestoreAlertRulesAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = serializer.Deserialize<List<AlertRule>>(payloadJson);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existingRules = await dbContext.AlertRules.ToListAsync(cancellationToken);
        foreach (var savedRule in payload)
        {
            var existing = existingRules.FirstOrDefault(rule => string.Equals(
                rule.Key,
                savedRule.Key,
                StringComparison.OrdinalIgnoreCase));
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
        var payload = serializer.Deserialize<BackupScheduleState>(payloadJson);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.BackupScheduleStates.SingleOrDefaultAsync(
            schedule => schedule.Id == 1,
            cancellationToken);
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
        var payload = serializer.Deserialize<AdminWebListenerSnapshotPayload>(payloadJson);
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
}
