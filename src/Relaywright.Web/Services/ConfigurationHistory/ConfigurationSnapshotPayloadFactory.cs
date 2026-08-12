using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Services.ConfigurationHistory;

public sealed class ConfigurationSnapshotPayloadFactory(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IAdminWebListenerConfigurationService adminWebListenerConfigurationService,
    ConfigurationSnapshotSerializer serializer)
{
    public async Task<ConfigurationSnapshotPayload> CreateAsync(
        string area,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return area switch
        {
            ConfigurationSnapshotAreas.Relay => new ConfigurationSnapshotPayload(
                "Relay Settings",
                serializer.Serialize(await dbContext.RelayConfigurations
                    .AsNoTracking()
                    .SingleAsync(cancellationToken))),
            ConfigurationSnapshotAreas.SubmissionPolicy => new ConfigurationSnapshotPayload(
                "Submission Policy",
                serializer.Serialize(await dbContext.SubmissionPolicies
                    .AsNoTracking()
                    .SingleAsync(policy => policy.Id == 1, cancellationToken))),
            ConfigurationSnapshotAreas.TrustedNetworks => new ConfigurationSnapshotPayload(
                "Trusted IPs",
                serializer.Serialize(await dbContext.TrustedNetworks
                    .AsNoTracking()
                    .OrderBy(network => network.Cidr)
                    .ToListAsync(cancellationToken))),
            ConfigurationSnapshotAreas.AlertRules => new ConfigurationSnapshotPayload(
                "Alerts",
                serializer.Serialize(await dbContext.AlertRules
                    .AsNoTracking()
                    .OrderBy(rule => rule.Key)
                    .ToListAsync(cancellationToken))),
            ConfigurationSnapshotAreas.BackupSchedule => new ConfigurationSnapshotPayload(
                "Backup Schedule",
                serializer.Serialize(await dbContext.BackupScheduleStates
                    .AsNoTracking()
                    .SingleAsync(schedule => schedule.Id == 1, cancellationToken))),
            ConfigurationSnapshotAreas.AdminWebListener => new ConfigurationSnapshotPayload(
                "Web Interface",
                serializer.Serialize(new AdminWebListenerSnapshotPayload(
                    await adminWebListenerConfigurationService.GetConfigurationAsync(cancellationToken)))),
            _ => throw new InvalidOperationException(
                $"Unsupported configuration snapshot area '{area}'.")
        };
    }
}
