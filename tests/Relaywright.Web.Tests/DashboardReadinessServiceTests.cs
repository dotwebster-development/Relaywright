using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Backups;
using Relaywright.Web.Services.ConfigurationHistory;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class DashboardReadinessServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task InitialStateShowsRequiredActionsAndIgnoresSeededLoopbackNetworks()
    {
        await using var store = await SqliteTestStore.CreateAsync();
        await using (var dbContext = store.CreateDbContext())
        {
            dbContext.TrustedNetworks.AddRange(
                new TrustedNetwork { Cidr = "127.0.0.1/32", Description = "Localhost IPv4" },
                new TrustedNetwork { Cidr = "::1/128", Description = "Localhost IPv6" });
            await dbContext.SaveChangesAsync();
        }

        var service = CreateService(store);
        var snapshot = await service.GetSnapshotAsync(
            new RelayConfigurationSnapshot { UpdatedUtc = DateTimeOffset.UtcNow },
            new BackupReadiness { IsReady = false, Message = "No validated backup is available." },
            CancellationToken.None);

        Assert.Equal(7, snapshot.RequiredCount);
        Assert.Equal(1, snapshot.CompletedRequiredCount);
        Assert.False(snapshot.IsReady);
        Assert.True(Item(snapshot, "admin-https").IsComplete);
        Assert.False(Item(snapshot, "upstream").IsComplete);
        Assert.False(Item(snapshot, "trusted-device").IsComplete);
        Assert.False(Item(snapshot, "submission-policy").IsComplete);
        Assert.False(Item(snapshot, "connectivity").IsComplete);
        Assert.False(Item(snapshot, "test-email").IsComplete);
        Assert.False(Item(snapshot, "backup").IsComplete);
        Assert.False(Item(snapshot, "alert-email").IsRequired);
        Assert.Equal("Optional", Item(snapshot, "alert-email").Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RecordedConfigurationAndSuccessfulChecksCompleteReadiness()
    {
        await using var store = await SqliteTestStore.CreateAsync();
        var configurationUpdatedUtc = DateTimeOffset.UtcNow.AddMinutes(-30);
        await using (var dbContext = store.CreateDbContext())
        {
            dbContext.TrustedNetworks.Add(new TrustedNetwork
            {
                Cidr = "192.0.2.25/32",
                Description = "Office printer"
            });
            dbContext.ConfigurationSnapshots.Add(new ConfigurationSnapshot
            {
                Id = Guid.NewGuid(),
                Area = ConfigurationSnapshotService.SubmissionPolicyArea,
                DisplayName = "Submission Policy",
                Summary = "Snapshot before save.",
                PayloadJson = "{}"
            });
            dbContext.DiagnosticRuns.AddRange(
                SuccessfulRun(DiagnosticRunKind.Connectivity, configurationUpdatedUtc.AddMinutes(5)),
                SuccessfulRun(DiagnosticRunKind.TestEmail, configurationUpdatedUtc.AddMinutes(10)));
            dbContext.AlertRules.Add(new AlertRule
            {
                Key = "queue-depth",
                DisplayName = "Queue depth",
                Description = "Queue depth warning.",
                EmailRecipients = "operator@example.test"
            });
            await dbContext.SaveChangesAsync();
        }

        var certificate = new AdminHttpsCertificateConfiguration
        {
            Mode = AdminHttpsCertificateMode.SelfSigned,
            CertificatePath = "admin.pfx",
            NotAfterUtc = DateTimeOffset.UtcNow.AddYears(1)
        };
        var service = CreateService(
            store,
            certificate,
            new AdminWebListenerConfiguration { EnableHttp = false });
        var snapshot = await service.GetSnapshotAsync(
            new RelayConfigurationSnapshot
            {
                UpdatedUtc = configurationUpdatedUtc,
                UpstreamHost = "smtp.example.test",
                UpstreamPort = 587
            },
            new BackupReadiness
            {
                IsReady = true,
                Message = "Last validated backup is 2 hour(s) old."
            },
            CancellationToken.None);

        Assert.True(snapshot.IsReady);
        Assert.Equal(snapshot.RequiredCount, snapshot.CompletedRequiredCount);
        Assert.Equal(100, snapshot.CompletionPercent);
        Assert.All(snapshot.Items, item => Assert.True(item.IsComplete));
        Assert.Equal("Passed", Item(snapshot, "connectivity").Status);
        Assert.Equal("Delivered", Item(snapshot, "test-email").Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DiagnosticsBeforeCurrentRelayConfigurationDoNotCount()
    {
        await using var store = await SqliteTestStore.CreateAsync();
        var configurationUpdatedUtc = DateTimeOffset.UtcNow;
        await using (var dbContext = store.CreateDbContext())
        {
            dbContext.DiagnosticRuns.AddRange(
                SuccessfulRun(DiagnosticRunKind.Connectivity, configurationUpdatedUtc.AddMinutes(-10)),
                SuccessfulRun(DiagnosticRunKind.TestEmail, configurationUpdatedUtc.AddMinutes(-5)));
            await dbContext.SaveChangesAsync();
        }

        var service = CreateService(
            store,
            new AdminHttpsCertificateConfiguration
            {
                Mode = AdminHttpsCertificateMode.SelfSigned,
                CertificatePath = "expired.pfx",
                NotAfterUtc = DateTimeOffset.UtcNow.AddDays(-1)
            },
            new AdminWebListenerConfiguration { EnableHttp = true });
        var snapshot = await service.GetSnapshotAsync(
            new RelayConfigurationSnapshot
            {
                UpdatedUtc = configurationUpdatedUtc,
                UpstreamHost = "smtp.example.test"
            },
            new BackupReadiness { IsReady = true, Message = "Ready." },
            CancellationToken.None);

        Assert.False(Item(snapshot, "admin-https").IsComplete);
        Assert.Contains("HTTP listener is enabled", Item(snapshot, "admin-https").Detail, StringComparison.Ordinal);
        Assert.False(Item(snapshot, "connectivity").IsComplete);
        Assert.False(Item(snapshot, "test-email").IsComplete);
    }

    private static DashboardReadinessService CreateService(
        SqliteTestStore store,
        AdminHttpsCertificateConfiguration? certificate = null,
        AdminWebListenerConfiguration? listener = null)
    {
        return new DashboardReadinessService(
            store.DbContextFactory,
            new StaticAdminHttpsCertificateService(certificate),
            new StaticAdminWebListenerConfigurationService(listener),
            NullLogger<DashboardReadinessService>.Instance);
    }

    private static DiagnosticRun SuccessfulRun(DiagnosticRunKind kind, DateTimeOffset completedUtc)
    {
        return new DiagnosticRun
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            StartedUtc = completedUtc.AddSeconds(-1),
            CompletedUtc = completedUtc,
            Succeeded = true,
            Message = "Succeeded."
        };
    }

    private static DashboardReadinessItem Item(DashboardReadinessSnapshot snapshot, string key)
    {
        return Assert.Single(snapshot.Items, x => x.Key == key);
    }

    private sealed class StaticAdminHttpsCertificateService(AdminHttpsCertificateConfiguration? configuration)
        : IAdminHttpsCertificateService
    {
        public Task<AdminHttpsCertificateConfiguration?> GetConfigurationAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(configuration);
        }

        public Task<AdminHttpsCertificateConfiguration> SavePfxAsync(
            IFormFile certificateFile,
            string? password,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AdminHttpsCertificateConfiguration> SavePemAsync(
            IFormFile certificateFile,
            IFormFile keyFile,
            string? keyPassword,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AdminHttpsCertificateConfiguration> GenerateSelfSignedAsync(
            string dnsNames,
            int validYears,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StaticAdminWebListenerConfigurationService(AdminWebListenerConfiguration? configuration)
        : IAdminWebListenerConfigurationService
    {
        public Task<AdminWebListenerConfiguration?> GetConfigurationAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(configuration);
        }

        public Task<AdminWebListenerConfiguration> SaveAsync(
            AdminWebListenerConfiguration configuration,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
