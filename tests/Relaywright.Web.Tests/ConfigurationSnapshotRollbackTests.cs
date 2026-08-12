using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.ConfigurationHistory;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class ConfigurationSnapshotRollbackTests
{
    [Fact]
    public async Task RollbackRestoresSubmissionPolicy()
    {
        await using var context = await ConfigurationSnapshotTestContext.CreateAsync();
        await using (var dbContext = context.Database.CreateDbContext())
        {
            dbContext.SubmissionPolicies.Add(new SubmissionPolicy
            {
                Id = 1,
                AllowedSenderAddresses = "@original.test",
                MaxMessageSizeBytes = 1024,
                IsEnabled = true
            });
            await dbContext.SaveChangesAsync();
        }

        var snapshot = await CaptureAsync(context, ConfigurationSnapshotAreas.SubmissionPolicy);
        await using (var dbContext = context.Database.CreateDbContext())
        {
            var policy = await dbContext.SubmissionPolicies.SingleAsync();
            policy.AllowedSenderAddresses = "@changed.test";
            policy.MaxMessageSizeBytes = 2048;
            await dbContext.SaveChangesAsync();
        }

        await context.Service.RollbackAsync(snapshot.Id, "admin", CancellationToken.None);

        await using var verifyContext = context.Database.CreateDbContext();
        var restored = await verifyContext.SubmissionPolicies.SingleAsync();
        Assert.Equal("@original.test", restored.AllowedSenderAddresses);
        Assert.Equal(1024, restored.MaxMessageSizeBytes);
    }

    [Fact]
    public async Task RollbackRestoresTrustedNetworks()
    {
        await using var context = await ConfigurationSnapshotTestContext.CreateAsync();
        await using (var dbContext = context.Database.CreateDbContext())
        {
            dbContext.TrustedNetworks.Add(new TrustedNetwork
            {
                Cidr = "192.0.2.10/32",
                Description = "Original scanner",
                AllowedSenderAddresses = "scanner@example.test",
                IsEnabled = true
            });
            await dbContext.SaveChangesAsync();
        }

        var snapshot = await CaptureAsync(context, ConfigurationSnapshotAreas.TrustedNetworks);
        await using (var dbContext = context.Database.CreateDbContext())
        {
            dbContext.TrustedNetworks.RemoveRange(dbContext.TrustedNetworks);
            await dbContext.SaveChangesAsync();
            dbContext.TrustedNetworks.Add(new TrustedNetwork
            {
                Cidr = "198.51.100.20/32",
                Description = "Replacement",
                IsEnabled = true
            });
            await dbContext.SaveChangesAsync();
        }

        await context.Service.RollbackAsync(snapshot.Id, "admin", CancellationToken.None);

        await using var verifyContext = context.Database.CreateDbContext();
        var restored = Assert.Single(await verifyContext.TrustedNetworks.AsNoTracking().ToListAsync());
        Assert.Equal("192.0.2.10/32", restored.Cidr);
        Assert.Equal("Original scanner", restored.Description);
        Assert.Equal("scanner@example.test", restored.AllowedSenderAddresses);
    }

    [Fact]
    public async Task RollbackRestoresAlertRuleState()
    {
        await using var context = await ConfigurationSnapshotTestContext.CreateAsync();
        await using (var dbContext = context.Database.CreateDbContext())
        {
            dbContext.AlertRules.Add(new AlertRule
            {
                Key = "queue-depth",
                DisplayName = "Queue depth",
                Description = "Original description",
                Threshold = 25,
                CooldownMinutes = 30,
                IsEnabled = true
            });
            await dbContext.SaveChangesAsync();
        }

        var snapshot = await CaptureAsync(context, ConfigurationSnapshotAreas.AlertRules);
        await using (var dbContext = context.Database.CreateDbContext())
        {
            var rule = await dbContext.AlertRules.SingleAsync();
            rule.Description = "Changed description";
            rule.Threshold = 100;
            rule.IsEnabled = false;
            await dbContext.SaveChangesAsync();
        }

        await context.Service.RollbackAsync(snapshot.Id, "admin", CancellationToken.None);

        await using var verifyContext = context.Database.CreateDbContext();
        var restored = await verifyContext.AlertRules.AsNoTracking().SingleAsync();
        Assert.Equal("Original description", restored.Description);
        Assert.Equal(25, restored.Threshold);
        Assert.True(restored.IsEnabled);
    }

    [Fact]
    public async Task RollbackRestoresBackupSchedule()
    {
        await using var context = await ConfigurationSnapshotTestContext.CreateAsync();
        await using (var dbContext = context.Database.CreateDbContext())
        {
            dbContext.BackupScheduleStates.Add(new BackupScheduleState
            {
                Id = 1,
                IsEnabled = true,
                IntervalHours = 12,
                RetentionCount = 14
            });
            await dbContext.SaveChangesAsync();
        }

        var snapshot = await CaptureAsync(context, ConfigurationSnapshotAreas.BackupSchedule);
        await using (var dbContext = context.Database.CreateDbContext())
        {
            var schedule = await dbContext.BackupScheduleStates.SingleAsync();
            schedule.IsEnabled = false;
            schedule.IntervalHours = 72;
            schedule.RetentionCount = 3;
            await dbContext.SaveChangesAsync();
        }

        await context.Service.RollbackAsync(snapshot.Id, "admin", CancellationToken.None);

        await using var verifyContext = context.Database.CreateDbContext();
        var restored = await verifyContext.BackupScheduleStates.AsNoTracking().SingleAsync();
        Assert.True(restored.IsEnabled);
        Assert.Equal(12, restored.IntervalHours);
        Assert.Equal(14, restored.RetentionCount);
    }

    [Fact]
    public async Task RollbackRestoresAdminWebListenerAndRequestsRestart()
    {
        await using var context = await ConfigurationSnapshotTestContext.CreateAsync();
        await context.AdminWebListenerConfigurationService.SaveAsync(
            new AdminWebListenerConfiguration
            {
                HttpsPort = 5443,
                EnableHttp = false,
                HttpPort = 5080
            },
            CancellationToken.None);

        var snapshot = await CaptureAsync(context, ConfigurationSnapshotAreas.AdminWebListener);
        await context.AdminWebListenerConfigurationService.SaveAsync(
            new AdminWebListenerConfiguration
            {
                HttpsPort = 7443,
                EnableHttp = true,
                HttpPort = 7080
            },
            CancellationToken.None);

        await context.Service.RollbackAsync(snapshot.Id, "admin", CancellationToken.None);

        var restored = await context.AdminWebListenerConfigurationService.GetConfigurationAsync(CancellationToken.None);
        Assert.NotNull(restored);
        Assert.Equal(5443, restored!.HttpsPort);
        Assert.False(restored.EnableHttp);
        var restart = Assert.Single(context.RestartService.Requests);
        Assert.Equal("Admin web listener settings were rolled back.", restart.Reason);
        Assert.Equal("admin", restart.UserName);
    }

    [Fact]
    public async Task PayloadFactoryRejectsUnsupportedArea()
    {
        await using var context = await ConfigurationSnapshotTestContext.CreateAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.CaptureAsync(
            "Unsupported",
            "admin",
            "Invalid area.",
            CancellationToken.None));

        Assert.Equal("Unsupported configuration snapshot area 'Unsupported'.", exception.Message);
    }

    [Fact]
    public async Task RestorerRejectsMalformedPayload()
    {
        await using var context = await ConfigurationSnapshotTestContext.CreateAsync();

        await Assert.ThrowsAsync<JsonException>(() => context.Restorer.RestoreAsync(
            ConfigurationSnapshotAreas.SubmissionPolicy,
            "{not-json}",
            "admin",
            CancellationToken.None));
    }

    private static async Task<ConfigurationSnapshot> CaptureAsync(
        ConfigurationSnapshotTestContext context,
        string area)
    {
        await context.Service.CaptureAsync(area, "admin", "Before change.", CancellationToken.None);
        return Assert.Single(await context.Service.GetRecentAsync(10, CancellationToken.None));
    }
}
