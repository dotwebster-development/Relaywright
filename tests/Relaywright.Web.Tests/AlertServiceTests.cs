using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Alerts;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class AlertServiceTests
{
    [Fact]
    public async Task EvaluateTriggersQueueDepthAlertAndRecordsNotificationResult()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        using var appData = TempAppData.Create();
        await using (var dbContext = database.CreateDbContext())
        {
            dbContext.AlertRules.Add(new AlertRule
            {
                Key = "queue-depth",
                DisplayName = "Queue depth",
                Description = "Queue is deep.",
                IsEnabled = true,
                Threshold = 1,
                CooldownMinutes = 60,
                EmailRecipients = "admin@example.test"
            });
            dbContext.QueuedMessages.Add(TestData.QueuedMessage());
            dbContext.QueuedMessages.Add(TestData.QueuedMessage(spoolPath: "message-2.eml"));
            await dbContext.SaveChangesAsync();
        }

        var notifier = new RecordingAlertEmailNotifier();
        var service = new AlertService(
            database.DbContextFactory,
            new StaticRuntimeStatusService(),
            new StaticRelayConfigurationService(TestData.Snapshot()),
            new NullAdminHttpsCertificateService(),
            notifier,
            new RecordingOperationalEventService(),
            appData.Paths,
            NullLogger<AlertService>.Instance);

        await service.EvaluateAsync(CancellationToken.None);

        await using var verifyContext = database.CreateDbContext();
        var rule = verifyContext.AlertRules.Single();
        var result = verifyContext.AlertResults.Single();
        Assert.True(rule.IsActive);
        Assert.True(rule.LastNotificationSucceeded);
        Assert.Equal(2, result.ObservedValue);
        Assert.True(result.NotificationSucceeded);
        Assert.Equal(1, notifier.SendCount);
    }

    [Fact]
    public async Task EvaluateOldestActiveMessageAndRecentResultsWorkWithSqliteDateTimeOffsets()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        using var appData = TempAppData.Create();
        var now = DateTimeOffset.UtcNow;
        await using (var dbContext = database.CreateDbContext())
        {
            dbContext.AlertRules.Add(new AlertRule
            {
                Key = "oldest-active-message-minutes",
                DisplayName = "Oldest active message age",
                Description = "Old message.",
                IsEnabled = true,
                Threshold = 30,
                CooldownMinutes = 60
            });
            dbContext.QueuedMessages.Add(TestData.QueuedMessage(acceptedUtc: now.AddMinutes(-90)));
            dbContext.QueuedMessages.Add(TestData.QueuedMessage(acceptedUtc: now.AddMinutes(-10), spoolPath: "newer.eml"));
            await dbContext.SaveChangesAsync();
        }

        var service = new AlertService(
            database.DbContextFactory,
            new StaticRuntimeStatusService(),
            new StaticRelayConfigurationService(TestData.Snapshot()),
            new NullAdminHttpsCertificateService(),
            new RecordingAlertEmailNotifier(),
            new RecordingOperationalEventService(),
            appData.Paths,
            NullLogger<AlertService>.Instance);

        await service.EvaluateAsync(CancellationToken.None);
        var recentResults = await service.GetRecentResultsAsync(1, CancellationToken.None);

        await using var verifyContext = database.CreateDbContext();
        var rule = verifyContext.AlertRules.Single();
        Assert.True(rule.IsActive);
        Assert.Single(recentResults);
        Assert.True(recentResults.Single().ObservedValue >= 89);
    }

    private sealed class RecordingAlertEmailNotifier : IAlertEmailNotifier
    {
        public int SendCount { get; private set; }

        public Task<AlertNotificationResult> SendAsync(
            AlertRule rule,
            string message,
            RelayConfigurationSnapshot configuration,
            CancellationToken cancellationToken)
        {
            SendCount += 1;
            return Task.FromResult(new AlertNotificationResult
            {
                Succeeded = true,
                Message = "sent"
            });
        }
    }

    private sealed class NullAdminHttpsCertificateService : IAdminHttpsCertificateService
    {
        public Task<AdminHttpsCertificateConfiguration?> GetConfigurationAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<AdminHttpsCertificateConfiguration?>(null);
        }

        public Task<AdminHttpsCertificateConfiguration> SavePfxAsync(IFormFile certificateFile, string? password, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<AdminHttpsCertificateConfiguration> SavePemAsync(IFormFile certificateFile, IFormFile keyFile, string? keyPassword, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<AdminHttpsCertificateConfiguration> GenerateSelfSignedAsync(string dnsNames, int validYears, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
