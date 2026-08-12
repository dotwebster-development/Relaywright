using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Alerts;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class AlertStateCoordinatorTests
{
    [Fact]
    public async Task InactiveToActiveTriggersNotificationEventAndResult()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var notifier = new RecordingNotifier();
        var events = new RecordingOperationalEventService();
        var coordinator = CreateCoordinator(notifier, events);
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await using var dbContext = database.CreateDbContext();
        var rule = await AddRuleAsync(dbContext, isActive: false);

        await coordinator.ApplyAsync(
            dbContext,
            rule,
            new AlertEvaluation(true, 5, "Threshold exceeded."),
            TestData.Snapshot(),
            now,
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.True(rule.IsActive);
        Assert.Equal(now, rule.LastTriggeredUtc);
        Assert.Equal(1, notifier.SendCount);
        Assert.Contains(events.Events, entry => entry.Message == "Alert triggered: Test rule.");
        var result = Assert.Single(await dbContext.AlertResults.AsNoTracking().ToListAsync());
        Assert.True(result.IsActive);
        Assert.True(result.NotificationSucceeded);
    }

    [Fact]
    public async Task ActiveAlertInsideCooldownDoesNotNotifyOrCreateResult()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var notifier = new RecordingNotifier();
        var events = new RecordingOperationalEventService();
        var coordinator = CreateCoordinator(notifier, events);
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await using var dbContext = database.CreateDbContext();
        var rule = await AddRuleAsync(dbContext, isActive: true, lastNotificationUtc: now.AddMinutes(-30));

        await coordinator.ApplyAsync(
            dbContext,
            rule,
            new AlertEvaluation(true, 5, "Still active."),
            TestData.Snapshot(),
            now,
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(0, notifier.SendCount);
        Assert.Empty(events.Events);
        Assert.Empty(await dbContext.AlertResults.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ActiveAlertAfterCooldownNotifiesAndRecordsResult()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var notifier = new RecordingNotifier();
        var events = new RecordingOperationalEventService();
        var coordinator = CreateCoordinator(notifier, events);
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await using var dbContext = database.CreateDbContext();
        var rule = await AddRuleAsync(dbContext, isActive: true, lastNotificationUtc: now.AddMinutes(-60));

        await coordinator.ApplyAsync(
            dbContext,
            rule,
            new AlertEvaluation(true, 5, "Still active."),
            TestData.Snapshot(),
            now,
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, notifier.SendCount);
        Assert.Empty(events.Events);
        Assert.Single(await dbContext.AlertResults.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ActiveToResolvedRecordsRecoveryWithoutNotification()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var notifier = new RecordingNotifier();
        var events = new RecordingOperationalEventService();
        var coordinator = CreateCoordinator(notifier, events);
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await using var dbContext = database.CreateDbContext();
        var rule = await AddRuleAsync(dbContext, isActive: true, lastNotificationUtc: now.AddMinutes(-10));

        await coordinator.ApplyAsync(
            dbContext,
            rule,
            new AlertEvaluation(false, 0, "Recovered."),
            TestData.Snapshot(),
            now,
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.False(rule.IsActive);
        Assert.Equal(now, rule.LastResolvedUtc);
        Assert.Equal(0, notifier.SendCount);
        Assert.Contains(events.Events, entry => entry.Message == "Alert resolved: Test rule.");
        Assert.False(Assert.Single(await dbContext.AlertResults.AsNoTracking().ToListAsync()).IsActive);
    }

    [Fact]
    public async Task NotificationFailureIsPersisted()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var notifier = new RecordingNotifier(succeeded: false, message: "SMTP unavailable");
        var coordinator = CreateCoordinator(notifier, new RecordingOperationalEventService());
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await using var dbContext = database.CreateDbContext();
        var rule = await AddRuleAsync(dbContext, isActive: false);

        await coordinator.ApplyAsync(
            dbContext,
            rule,
            new AlertEvaluation(true, 5, "Threshold exceeded."),
            TestData.Snapshot(),
            now,
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.False(rule.LastNotificationSucceeded);
        Assert.Equal("SMTP unavailable", rule.LastNotificationMessage);
        var result = Assert.Single(await dbContext.AlertResults.AsNoTracking().ToListAsync());
        Assert.False(result.NotificationSucceeded);
        Assert.Equal("SMTP unavailable", result.NotificationMessage);
    }

    private static AlertStateCoordinator CreateCoordinator(
        IAlertEmailNotifier notifier,
        IOperationalEventService events)
    {
        return new AlertStateCoordinator(
            notifier,
            events,
            NullLogger<AlertStateCoordinator>.Instance);
    }

    private static async Task<AlertRule> AddRuleAsync(
        Relaywright.Web.Data.ApplicationDbContext dbContext,
        bool isActive,
        DateTimeOffset? lastNotificationUtc = null)
    {
        var rule = new AlertRule
        {
            Key = "test-rule",
            DisplayName = "Test rule",
            Description = "Test rule.",
            IsEnabled = true,
            Threshold = 1,
            CooldownMinutes = 60,
            EmailRecipients = "admin@example.test",
            IsActive = isActive,
            LastNotificationUtc = lastNotificationUtc
        };
        dbContext.AlertRules.Add(rule);
        await dbContext.SaveChangesAsync();
        return rule;
    }

    private sealed class RecordingNotifier(bool succeeded = true, string message = "sent") : IAlertEmailNotifier
    {
        public int SendCount { get; private set; }

        public Task<AlertNotificationResult> SendAsync(
            AlertRule rule,
            string alertMessage,
            RelayConfigurationSnapshot configuration,
            CancellationToken cancellationToken)
        {
            SendCount += 1;
            return Task.FromResult(new AlertNotificationResult
            {
                Succeeded = succeeded,
                Message = message
            });
        }
    }
}
