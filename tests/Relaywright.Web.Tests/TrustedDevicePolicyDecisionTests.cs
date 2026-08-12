using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class TrustedDevicePolicyDecisionTests
{
    [Fact]
    public async Task ProfileBlockListTakesPrecedenceOverAllowLists()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var service = CreateService(database);
        var profile = new TrustedNetwork
        {
            AllowedSenderAddresses = "@example.test",
            BlockedSenderAddresses = "blocked@example.test"
        };
        var policy = new SubmissionPolicy
        {
            AllowedSenderAddresses = "@example.test",
            IsEnabled = true
        };

        var decision = service.CanAcceptFrom(profile, policy, "blocked@example.test", 100);

        Assert.False(decision.Allowed);
        Assert.Equal("Sender is blocked by submission policy.", decision.Message);
    }

    [Fact]
    public async Task DisabledGlobalPolicyDoesNotApplyListsOrLimits()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var service = CreateService(database);
        var profile = new TrustedNetwork();
        var policy = new SubmissionPolicy
        {
            BlockedSenderAddresses = "@example.test",
            MaxMessageSizeBytes = 10,
            IsEnabled = false
        };

        var decision = service.CanAcceptFrom(profile, policy, "sender@example.test", 100);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task StricterMessageSizeLimitWins()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var service = CreateService(database);
        var profile = new TrustedNetwork { MaxMessageSizeBytes = 2048 };
        var policy = new SubmissionPolicy
        {
            MaxMessageSizeBytes = 1024,
            IsEnabled = true
        };

        var decision = service.CanAcceptFrom(profile, policy, "sender@example.test", 1025);

        Assert.False(decision.Allowed);
        Assert.Contains("1024 bytes", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SenderMatchingNormalizesDisplayNameAndCase()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var service = CreateService(database);
        var profile = new TrustedNetwork
        {
            AllowedSenderAddresses = "scanner@example.test"
        };

        var decision = service.CanAcceptFrom(
            profile,
            new SubmissionPolicy { IsEnabled = false },
            "\"Office Scanner\" <SCANNER@EXAMPLE.TEST>",
            100);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task WildcardRecipientDomainMatchesSubdomainButNotApex()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var service = CreateService(database);
        var profile = new TrustedNetwork
        {
            AllowedRecipientDomains = "*.partner.test"
        };
        var policy = new SubmissionPolicy { IsEnabled = false };

        var subdomainDecision = service.CanDeliverTo(profile, policy, "user@mail.partner.test", 1);
        var apexDecision = service.CanDeliverTo(profile, policy, "user@partner.test", 1);

        Assert.True(subdomainDecision.Allowed);
        Assert.False(apexDecision.Allowed);
        Assert.Equal("Recipient domain is not allowed by submission policy.", apexDecision.Message);
    }

    [Fact]
    public async Task GlobalRecipientBlockTakesPrecedenceOverProfileAllowList()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var service = CreateService(database);
        var profile = new TrustedNetwork
        {
            AllowedRecipientDomains = "blocked.test"
        };
        var policy = new SubmissionPolicy
        {
            BlockedRecipientDomains = "blocked.test",
            IsEnabled = true
        };

        var decision = service.CanDeliverTo(profile, policy, "user@blocked.test", 1);

        Assert.False(decision.Allowed);
        Assert.Equal("Recipient domain is blocked by submission policy.", decision.Message);
    }

    [Fact]
    public async Task StricterRecipientLimitWins()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var service = CreateService(database);
        var profile = new TrustedNetwork { MaxRecipientsPerMessage = 5 };
        var policy = new SubmissionPolicy
        {
            MaxRecipientsPerMessage = 2,
            IsEnabled = true
        };

        var decision = service.CanDeliverTo(profile, policy, "user@example.test", 3);

        Assert.False(decision.Allowed);
        Assert.Contains("recipient limit of 2", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecipientWithoutDomainIsDenied()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var service = CreateService(database);

        var decision = service.CanDeliverTo(
            new TrustedNetwork(),
            new SubmissionPolicy { IsEnabled = false },
            "local-recipient",
            1);

        Assert.False(decision.Allowed);
        Assert.Equal("Recipient address does not include a domain.", decision.Message);
    }

    [Fact]
    public async Task EmptyAllowListsDoNotRestrictSubmission()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var service = CreateService(database);

        var senderDecision = service.CanAcceptFrom(
            new TrustedNetwork(),
            new SubmissionPolicy { IsEnabled = true },
            "sender@example.test",
            100);
        var recipientDecision = service.CanDeliverTo(
            new TrustedNetwork(),
            new SubmissionPolicy { IsEnabled = true },
            "recipient@example.test",
            1);

        Assert.True(senderDecision.Allowed);
        Assert.True(recipientDecision.Allowed);
    }

    private static TrustedDevicePolicyService CreateService(SqliteTestStore database)
    {
        return new TrustedDevicePolicyService(
            database.DbContextFactory,
            new RecordingOperationalEventService(),
            new SubmissionPolicyEvaluator(),
            new SubmissionPolicyValidator(),
            NullLogger<TrustedDevicePolicyService>.Instance);
    }
}
