using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Services.Smtp;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class TrustedNetworkIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task MailboxFilterAcceptsEnabledCidrAndRejectsDisabledOrUntrustedIps()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        await using (var dbContext = database.CreateDbContext())
        {
            dbContext.TrustedNetworks.AddRange(
                new TrustedNetwork
                {
                    Cidr = "10.10.0.0/16",
                    Description = "enabled",
                    IsEnabled = true
                },
                new TrustedNetwork
                {
                    Cidr = "192.0.2.0/24",
                    Description = "disabled",
                    IsEnabled = false
                });
            await dbContext.SaveChangesAsync();
        }

        var events = new RecordingOperationalEventService();
        var filter = CreateFilter(database, events);

        Assert.True(await filter.CanAcceptFromAsync(
            new FakeSessionContext(IPAddress.Parse("10.10.2.3")),
            new FakeMailbox("sender@example.test"),
            100,
            CancellationToken.None));
        Assert.False(await filter.CanAcceptFromAsync(
            new FakeSessionContext(IPAddress.Parse("192.0.2.10")),
            new FakeMailbox("sender@example.test"),
            100,
            CancellationToken.None));
        Assert.False(await filter.CanAcceptFromAsync(
            new FakeSessionContext(IPAddress.Parse("203.0.113.10")),
            new FakeMailbox("sender@example.test"),
            100,
            CancellationToken.None));

        Assert.Equal(2, events.Events.Count(x => x.Category == OperationalEventCategory.Security));
    }

    [Fact]
    public async Task MailboxFilterRejectsSenderOutsideTrustedDeviceAllowList()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        await using (var dbContext = database.CreateDbContext())
        {
            dbContext.TrustedNetworks.Add(new TrustedNetwork
            {
                Cidr = "10.20.0.0/16",
                Description = "profiled scanner",
                AllowedSenderAddresses = "scanner@example.test",
                IsEnabled = true
            });
            await dbContext.SaveChangesAsync();
        }

        var events = new RecordingOperationalEventService();
        var filter = CreateFilter(database, events);
        var session = new FakeSessionContext(IPAddress.Parse("10.20.1.2"));

        Assert.False(await filter.CanAcceptFromAsync(
            session,
            new FakeMailbox("other@example.test"),
            100,
            CancellationToken.None));
        Assert.True(await filter.CanAcceptFromAsync(
            new FakeSessionContext(IPAddress.Parse("10.20.1.2")),
            new FakeMailbox("scanner@example.test"),
            100,
            CancellationToken.None));

        Assert.Contains(events.Events, x => x.Message == "Sender is not allowed by submission policy.");
    }

    [Fact]
    public async Task MailboxFilterRejectsRecipientBlockedByGlobalPolicy()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        await using (var dbContext = database.CreateDbContext())
        {
            dbContext.TrustedNetworks.Add(new TrustedNetwork
            {
                Cidr = "10.30.0.0/16",
                Description = "trusted subnet",
                IsEnabled = true
            });
            dbContext.SubmissionPolicies.Add(new SubmissionPolicy
            {
                BlockedRecipientDomains = "blocked.test",
                IsEnabled = true
            });
            await dbContext.SaveChangesAsync();
        }

        var events = new RecordingOperationalEventService();
        var filter = CreateFilter(database, events);
        var session = new FakeSessionContext(IPAddress.Parse("10.30.1.2"));

        Assert.True(await filter.CanAcceptFromAsync(
            session,
            new FakeMailbox("sender@example.test"),
            100,
            CancellationToken.None));
        Assert.False(await filter.CanDeliverToAsync(
            session,
            new FakeMailbox("recipient@blocked.test"),
            new FakeMailbox("sender@example.test"),
            CancellationToken.None));

        Assert.Contains(events.Events, x => x.Message == "Recipient domain is blocked by submission policy.");
    }

    [Fact]
    public async Task MailboxFilterEnforcesTrustedDeviceRecipientLimitAcrossAcceptedRecipients()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        await using (var dbContext = database.CreateDbContext())
        {
            dbContext.TrustedNetworks.Add(new TrustedNetwork
            {
                Cidr = "10.40.0.0/16",
                Description = "limited profile",
                MaxRecipientsPerMessage = 2,
                IsEnabled = true
            });
            await dbContext.SaveChangesAsync();
        }

        var events = new RecordingOperationalEventService();
        var filter = CreateFilter(database, events);
        var session = new FakeSessionContext(IPAddress.Parse("10.40.1.2"));
        var sender = new FakeMailbox("sender@example.test");

        Assert.True(await filter.CanAcceptFromAsync(session, sender, 100, CancellationToken.None));
        Assert.True(await filter.CanDeliverToAsync(session, new FakeMailbox("first@example.test"), sender, CancellationToken.None));
        Assert.True(await filter.CanDeliverToAsync(session, new FakeMailbox("second@example.test"), sender, CancellationToken.None));
        Assert.False(await filter.CanDeliverToAsync(session, new FakeMailbox("third@example.test"), sender, CancellationToken.None));

        Assert.Contains(events.Events, x => x.Message == "Message exceeds the allowed recipient limit of 2.");
    }

    [Fact]
    public async Task MailboxFilterEnforcesTrustedDeviceRateLimit()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        await using (var dbContext = database.CreateDbContext())
        {
            dbContext.TrustedNetworks.Add(new TrustedNetwork
            {
                Cidr = "10.50.0.0/16",
                Description = "rate limited profile",
                RateLimitMessagesPerHour = 1,
                IsEnabled = true
            });
            await dbContext.SaveChangesAsync();
        }

        var events = new RecordingOperationalEventService();
        var filter = CreateFilter(database, events);
        var remoteAddress = IPAddress.Parse("10.50.1.2");

        Assert.True(await filter.CanAcceptFromAsync(
            new FakeSessionContext(remoteAddress),
            new FakeMailbox("sender@example.test"),
            100,
            CancellationToken.None));
        Assert.False(await filter.CanAcceptFromAsync(
            new FakeSessionContext(remoteAddress),
            new FakeMailbox("sender@example.test"),
            100,
            CancellationToken.None));

        Assert.Contains(events.Events, x => x.Message == "Device profile rate limit exceeded (1 message(s) per hour).");
    }

    [Fact]
    public async Task AddOrUpdateRejectsOverlappingTrustedNetworks()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var trustedNetworkService = CreateTrustedNetworkService(database, new RecordingOperationalEventService());
        await trustedNetworkService.AddOrUpdateAsync(
            new TrustedNetwork
            {
                Cidr = "10.0.0.0/8",
                Description = "broad",
                IsEnabled = false
            },
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => trustedNetworkService.AddOrUpdateAsync(
            new TrustedNetwork
            {
                Cidr = "10.10.0.0/16",
                Description = "specific",
                IsEnabled = true
            },
            CancellationToken.None));

        Assert.Contains("overlaps", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FindMatchingUsesMostSpecificRangeWhenLegacyDataOverlaps()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        await using (var dbContext = database.CreateDbContext())
        {
            dbContext.TrustedNetworks.AddRange(
                new TrustedNetwork
                {
                    Cidr = "10.0.0.0/8",
                    Description = "broad",
                    IsEnabled = true
                },
                new TrustedNetwork
                {
                    Cidr = "10.10.0.0/16",
                    Description = "specific",
                    IsEnabled = true
                });
            await dbContext.SaveChangesAsync();
        }

        var trustedNetworkService = CreateTrustedNetworkService(database, new RecordingOperationalEventService());

        var match = await trustedNetworkService.FindMatchingAsync(IPAddress.Parse("10.10.1.2"), CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal("specific", match!.Description);
    }

    [Fact]
    public async Task PolicySummaryShowsEffectiveLimitsAndRuleCounts()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var events = new RecordingOperationalEventService();
        var policyService = new TrustedDevicePolicyService(
            database.DbContextFactory,
            events,
            NullLogger<TrustedDevicePolicyService>.Instance);

        var summary = policyService.DescribeEffectivePolicy(
            new TrustedNetwork
            {
                Id = 5,
                MaxMessageSizeBytes = 1024,
                MaxRecipientsPerMessage = 10,
                AllowedSenderAddresses = "scanner@example.test",
                BlockedRecipientDomains = "blocked.test"
            },
            new SubmissionPolicy
            {
                IsEnabled = true,
                MaxMessageSizeBytes = 2048,
                MaxRecipientsPerMessage = 3,
                BlockedSenderAddresses = "blocked@example.test",
                AllowedRecipientDomains = "example.test"
            });

        Assert.Equal(5, summary.TrustedNetworkId);
        Assert.Equal(1024, summary.MessageSizeLimit.Value);
        Assert.Contains("Trusted device", summary.MessageSizeLimit.Source);
        Assert.Equal(3, summary.RecipientLimit.Value);
        Assert.Contains("Global policy", summary.RecipientLimit.Source);
        Assert.Equal(2, summary.AllowedSenderRuleCount + summary.BlockedSenderRuleCount);
        Assert.Equal(2, summary.AllowedRecipientDomainRuleCount + summary.BlockedRecipientDomainRuleCount);
    }

    private static TrustedNetworkMailboxFilter CreateFilter(
        SqliteTestStore database,
        RecordingOperationalEventService events)
    {
        var trustedNetworkService = CreateTrustedNetworkService(database, events);
        var policyService = new TrustedDevicePolicyService(
            database.DbContextFactory,
            events,
            NullLogger<TrustedDevicePolicyService>.Instance);

        return new TrustedNetworkMailboxFilter(
            trustedNetworkService,
            policyService,
            new TrustedDeviceRateLimiter(),
            events,
            NullLogger<TrustedNetworkMailboxFilter>.Instance);
    }

    private static TrustedNetworkService CreateTrustedNetworkService(
        SqliteTestStore database,
        RecordingOperationalEventService events)
    {
        return new TrustedNetworkService(
            database.DbContextFactory,
            events,
            NullLogger<TrustedNetworkService>.Instance);
    }
}
