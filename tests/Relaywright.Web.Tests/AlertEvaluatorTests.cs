using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Alerts;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class AlertEvaluatorTests
{
    [Fact]
    public async Task QueueDepthCountsOnlyActiveStates()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        using var appData = TempAppData.Create();
        await using var dbContext = database.CreateDbContext();
        dbContext.QueuedMessages.AddRange(
            TestData.QueuedMessage(QueuedMessageStatus.Pending),
            TestData.QueuedMessage(QueuedMessageStatus.RetryScheduled, spoolPath: "retry.eml"),
            TestData.QueuedMessage(QueuedMessageStatus.Delivered, spoolPath: "delivered.eml"));
        await dbContext.SaveChangesAsync();

        var evaluation = await new AlertEvaluator(appData.Paths).EvaluateAsync(
            dbContext,
            Rule("queue-depth", threshold: 2),
            new RuntimeStatusSnapshot(),
            adminCertificate: null,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.True(evaluation.IsActive);
        Assert.Equal(2, evaluation.ObservedValue);
    }

    [Fact]
    public async Task OldestActiveMessageUsesOldestAcceptedTimestamp()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        using var appData = TempAppData.Create();
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await using var dbContext = database.CreateDbContext();
        dbContext.QueuedMessages.AddRange(
            TestData.QueuedMessage(acceptedUtc: now.AddMinutes(-90)),
            TestData.QueuedMessage(acceptedUtc: now.AddMinutes(-10), spoolPath: "newer.eml"));
        await dbContext.SaveChangesAsync();

        var evaluation = await new AlertEvaluator(appData.Paths).EvaluateAsync(
            dbContext,
            Rule("oldest-active-message-minutes", threshold: 30),
            new RuntimeStatusSnapshot(),
            adminCertificate: null,
            now,
            CancellationToken.None);

        Assert.True(evaluation.IsActive);
        Assert.Equal(90, evaluation.ObservedValue);
    }

    [Fact]
    public async Task FailedMessageCountIncludesFailedAndExpired()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        using var appData = TempAppData.Create();
        await using var dbContext = database.CreateDbContext();
        dbContext.QueuedMessages.AddRange(
            TestData.QueuedMessage(QueuedMessageStatus.Failed),
            TestData.QueuedMessage(QueuedMessageStatus.Expired, spoolPath: "expired.eml"),
            TestData.QueuedMessage(QueuedMessageStatus.Delivered, spoolPath: "delivered.eml"));
        await dbContext.SaveChangesAsync();

        var evaluation = await new AlertEvaluator(appData.Paths).EvaluateAsync(
            dbContext,
            Rule("failed-message-count", threshold: 2),
            new RuntimeStatusSnapshot(),
            adminCertificate: null,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.True(evaluation.IsActive);
        Assert.Equal(2, evaluation.ObservedValue);
    }

    [Fact]
    public async Task ListenerDownUsesRuntimeStatus()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        using var appData = TempAppData.Create();
        await using var dbContext = database.CreateDbContext();

        var evaluation = await new AlertEvaluator(appData.Paths).EvaluateAsync(
            dbContext,
            Rule("listener-down", threshold: 1),
            new RuntimeStatusSnapshot
            {
                SmtpListener = new RuntimeComponentState { Status = "Stopped" }
            },
            adminCertificate: null,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.True(evaluation.IsActive);
        Assert.Equal(1, evaluation.ObservedValue);
    }

    [Fact]
    public async Task DiskFreeReportsCurrentDataVolume()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        using var appData = TempAppData.Create();
        await using var dbContext = database.CreateDbContext();

        var evaluation = await new AlertEvaluator(appData.Paths).EvaluateAsync(
            dbContext,
            Rule("disk-free-mb", threshold: long.MaxValue),
            new RuntimeStatusSnapshot(),
            adminCertificate: null,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.True(evaluation.IsActive);
        Assert.True(evaluation.ObservedValue >= 0);
        Assert.Contains("free space", evaluation.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CertificateExpiryRoundsPartialDaysUp()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        using var appData = TempAppData.Create();
        await using var dbContext = database.CreateDbContext();
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var evaluation = await new AlertEvaluator(appData.Paths).EvaluateAsync(
            dbContext,
            Rule("admin-certificate-expiry-days", threshold: 6),
            new RuntimeStatusSnapshot(),
            new AdminHttpsCertificateConfiguration { NotAfterUtc = now.AddDays(5).AddHours(1) },
            now,
            CancellationToken.None);

        Assert.True(evaluation.IsActive);
        Assert.Equal(6, evaluation.ObservedValue);
    }

    [Fact]
    public async Task RecentUpstreamFailuresUsesOneHourWindow()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        using var appData = TempAppData.Create();
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await using var dbContext = database.CreateDbContext();
        var message = TestData.QueuedMessage();
        message.DeliveryAttempts.Add(new DeliveryAttempt
        {
            AttemptNumber = 1,
            StartedUtc = now.AddMinutes(-31),
            CompletedUtc = now.AddMinutes(-30),
            Succeeded = false
        });
        message.DeliveryAttempts.Add(new DeliveryAttempt
        {
            AttemptNumber = 2,
            StartedUtc = now.AddHours(-3),
            CompletedUtc = now.AddHours(-2),
            Succeeded = false
        });
        dbContext.QueuedMessages.Add(message);
        await dbContext.SaveChangesAsync();

        var evaluation = await new AlertEvaluator(appData.Paths).EvaluateAsync(
            dbContext,
            Rule("recent-upstream-failures", threshold: 1),
            new RuntimeStatusSnapshot(),
            adminCertificate: null,
            now,
            CancellationToken.None);

        Assert.True(evaluation.IsActive);
        Assert.Equal(1, evaluation.ObservedValue);
    }

    [Fact]
    public async Task UnknownRuleIsInactiveWithDiagnosticMessage()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        using var appData = TempAppData.Create();
        await using var dbContext = database.CreateDbContext();

        var evaluation = await new AlertEvaluator(appData.Paths).EvaluateAsync(
            dbContext,
            Rule("future-rule", threshold: 1),
            new RuntimeStatusSnapshot(),
            adminCertificate: null,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.False(evaluation.IsActive);
        Assert.Contains("Unknown alert rule", evaluation.Message, StringComparison.Ordinal);
    }

    private static AlertRule Rule(string key, long threshold)
    {
        return new AlertRule
        {
            Key = key,
            DisplayName = key,
            Threshold = threshold,
            CooldownMinutes = 60,
            IsEnabled = true
        };
    }
}
