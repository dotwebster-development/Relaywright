using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Diagnostics;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class SubmissionFlowCheckerTests
{
    [Fact]
    public async Task FlowCheckerRejectsUntrustedSourceIp()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var checker = CreateChecker(database);

        var result = await checker.CheckAsync(new SubmissionFlowCheckRequest
        {
            SourceIpAddress = "10.0.0.10",
            EnvelopeFrom = "scanner@example.test",
            Recipients = "recipient@example.test",
            DeclaredSizeBytes = 1024
        }, "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("trusted network", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FlowCheckerUsesPolicyAndDoesNotConsumeRateLimitPreview()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        await using (var dbContext = database.CreateDbContext())
        {
            dbContext.TrustedNetworks.Add(new TrustedNetwork
            {
                Cidr = "192.168.10.25/32",
                Description = "Scanner",
                AllowedSenderAddresses = "@example.test",
                AllowedRecipientDomains = "example.test",
                RateLimitMessagesPerHour = 1,
                IsEnabled = true
            });
            dbContext.SubmissionPolicies.Add(new SubmissionPolicy
            {
                Id = 1,
                IsEnabled = true,
                MaxMessageSizeBytes = 2048,
                MaxRecipientsPerMessage = 2
            });
            await dbContext.SaveChangesAsync();
        }

        var checker = CreateChecker(database);
        var request = new SubmissionFlowCheckRequest
        {
            SourceIpAddress = "192.168.10.25",
            EnvelopeFrom = "scanner@example.test",
            Recipients = "recipient@example.test",
            DeclaredSizeBytes = 1024
        };

        var first = await checker.CheckAsync(request, "admin", CancellationToken.None);
        var second = await checker.CheckAsync(request, "admin", CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
    }

    [Fact]
    public async Task FlowCheckerRejectsRecipientLimit()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        await using (var dbContext = database.CreateDbContext())
        {
            dbContext.TrustedNetworks.Add(new TrustedNetwork
            {
                Cidr = "192.168.10.25/32",
                Description = "Scanner",
                IsEnabled = true
            });
            dbContext.SubmissionPolicies.Add(new SubmissionPolicy
            {
                Id = 1,
                IsEnabled = true,
                MaxRecipientsPerMessage = 1
            });
            await dbContext.SaveChangesAsync();
        }

        var checker = CreateChecker(database);
        var result = await checker.CheckAsync(new SubmissionFlowCheckRequest
        {
            SourceIpAddress = "192.168.10.25",
            EnvelopeFrom = "scanner@example.test",
            Recipients = "one@example.test;two@example.test",
            DeclaredSizeBytes = 1024
        }, "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("recipient limit", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FlowEvaluatorUsesPolicyOverrideWithoutSavingPolicy()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        await using (var dbContext = database.CreateDbContext())
        {
            dbContext.TrustedNetworks.Add(new TrustedNetwork
            {
                Cidr = "192.168.10.25/32",
                Description = "Scanner",
                IsEnabled = true
            });
            dbContext.SubmissionPolicies.Add(new SubmissionPolicy
            {
                Id = 1,
                IsEnabled = true,
                MaxRecipientsPerMessage = 10
            });
            await dbContext.SaveChangesAsync();
        }

        var evaluator = CreateEvaluator(database);
        var result = await evaluator.EvaluateAsync(
            new SubmissionFlowCheckRequest
            {
                SourceIpAddress = "192.168.10.25",
                EnvelopeFrom = "scanner@example.test",
                Recipients = "one@example.test;two@example.test",
                DeclaredSizeBytes = 1024
            },
            CancellationToken.None,
            new SubmissionPolicy
            {
                Id = 1,
                IsEnabled = true,
                MaxRecipientsPerMessage = 1
            });

        Assert.False(result.Succeeded);
        Assert.Contains("recipient limit", result.Message, StringComparison.OrdinalIgnoreCase);

        await using var verifyContext = database.CreateDbContext();
        Assert.Equal(10, verifyContext.SubmissionPolicies.Single().MaxRecipientsPerMessage);
    }

    private static SubmissionFlowChecker CreateChecker(SqliteTestStore database)
    {
        var evaluator = CreateEvaluator(database);
        return new SubmissionFlowChecker(
            evaluator,
            new DiagnosticRunRecorder(
                database.DbContextFactory,
                NullLogger<DiagnosticRunRecorder>.Instance),
            NullLogger<SubmissionFlowChecker>.Instance);
    }

    private static SubmissionFlowEvaluator CreateEvaluator(SqliteTestStore database)
    {
        var events = new RecordingOperationalEventService();
        return new SubmissionFlowEvaluator(
            new TrustedNetworkService(
                database.DbContextFactory,
                events,
                NullLogger<TrustedNetworkService>.Instance),
            new TrustedDevicePolicyService(
                database.DbContextFactory,
                events,
                NullLogger<TrustedDevicePolicyService>.Instance),
            new TrustedDeviceRateLimiter());
    }
}
