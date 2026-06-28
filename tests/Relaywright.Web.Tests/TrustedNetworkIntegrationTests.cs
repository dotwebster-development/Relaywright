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
        var service = new TrustedNetworkService(
            database.DbContextFactory,
            events,
            NullLogger<TrustedNetworkService>.Instance);
        var filter = new TrustedNetworkMailboxFilter(
            service,
            events,
            NullLogger<TrustedNetworkMailboxFilter>.Instance);

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
}
