using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Tests.Support;
using Xunit;

using DashboardIndexModel = Relaywright.Web.Pages.IndexModel;
using LogsIndexModel = Relaywright.Web.Pages.Logs.IndexModel;
using QueueIndexModel = Relaywright.Web.Pages.Queue.IndexModel;

namespace Relaywright.Web.Tests;

public sealed class RazorPageOrderingTests
{
    [Fact]
    public async Task QueuePageOrdersMessagesByAcceptedUtcWithSqlite()
    {
        await using var fixture = await PageFixture.CreateAsync();
        var older = await fixture.AddQueuedMessageAsync(DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = await fixture.AddQueuedMessageAsync(DateTimeOffset.UtcNow);
        var model = fixture.CreateQueueModel();

        await model.OnGetAsync(null, CancellationToken.None);

        Assert.Equal(2, model.TotalCount);
        Assert.Equal([newer, older], model.Messages.Select(x => x.Id));
    }

    [Fact]
    public async Task QueuePagePaginatesInSqliteOrder()
    {
        await using var fixture = await PageFixture.CreateAsync();
        var ids = new List<Guid>();
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        for (var i = 0; i < 55; i++)
        {
            ids.Add(await fixture.AddQueuedMessageAsync(start.AddMinutes(i)));
        }

        var model = fixture.CreateQueueModel();
        model.PageNumber = 2;

        await model.OnGetAsync(null, CancellationToken.None);

        Assert.Equal(55, model.TotalCount);
        Assert.Equal(2, model.TotalPages);
        Assert.Equal(ids.Take(5).Reverse(), model.Messages.Select(x => x.Id));
    }

    [Fact]
    public async Task LogsPageOrdersEventsByOccurredUtcWithSqlite()
    {
        await using var fixture = await PageFixture.CreateAsync();
        await fixture.AddOperationalEventAsync("older", DateTimeOffset.UtcNow.AddMinutes(-10));
        await fixture.AddOperationalEventAsync("newer", DateTimeOffset.UtcNow);
        var model = fixture.CreateLogsModel();

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(2, model.TotalCount);
        Assert.Equal(["newer", "older"], model.Events.Select(x => x.Message));
    }

    [Fact]
    public async Task LogsPagePaginatesInSqliteOrder()
    {
        await using var fixture = await PageFixture.CreateAsync();
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        for (var i = 0; i < 55; i++)
        {
            await fixture.AddOperationalEventAsync($"event-{i}", start.AddMinutes(i));
        }

        var model = fixture.CreateLogsModel();
        model.PageNumber = 2;

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(55, model.TotalCount);
        Assert.Equal(2, model.TotalPages);
        Assert.Equal(Enumerable.Range(0, 5).Reverse().Select(x => $"event-{x}"), model.Events.Select(x => x.Message));
    }

    [Fact]
    public async Task DashboardOrdersRecentEventsByOccurredUtcWithSqlite()
    {
        await using var fixture = await PageFixture.CreateAsync();
        await fixture.AddOperationalEventAsync("older", DateTimeOffset.UtcNow.AddMinutes(-10));
        await fixture.AddOperationalEventAsync("newer", DateTimeOffset.UtcNow);
        var model = fixture.CreateDashboardModel();

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(["newer", "older"], model.RecentEvents.Select(x => x.Message));
    }

    [Fact]
    public async Task DashboardLoadsSuspiciousLoginSummary()
    {
        await using var fixture = await PageFixture.CreateAsync();
        var summary = new SuspiciousLoginSummary(
            true,
            FailedLast15Minutes: 5,
            FailedLast24Hours: 5,
            MostActiveRemoteIpAddress: "203.0.113.10",
            MostActiveRemoteIpFailureCount: 3,
            [new SuspiciousLoginFinding("Failed logins", "5 failed admin sign-ins in the last 15 minutes.", "severity-warning")]);
        var model = fixture.CreateDashboardModel(summary);

        await model.OnGetAsync(CancellationToken.None);

        Assert.True(model.SuspiciousLogins.IsSuspicious);
        Assert.Equal(5, model.SuspiciousLogins.FailedLast15Minutes);
    }

    private sealed class PageFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly TestDbContextFactory _dbContextFactory;

        private PageFixture(SqliteConnection connection, TestDbContextFactory dbContextFactory)
        {
            _connection = connection;
            _dbContextFactory = dbContextFactory;
        }

        public static async Task<PageFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var factory = new TestDbContextFactory(options);
            await using var dbContext = factory.CreateDbContext();
            await dbContext.Database.EnsureCreatedAsync();

            return new PageFixture(connection, factory);
        }

        public async Task<Guid> AddQueuedMessageAsync(DateTimeOffset acceptedUtc)
        {
            await using var dbContext = _dbContextFactory.CreateDbContext();
            var message = new QueuedMessage
            {
                Id = Guid.NewGuid(),
                SessionId = Guid.NewGuid(),
                EnvelopeFrom = "sender@example.test",
                SpoolFileRelativePath = $"{Guid.NewGuid():N}.eml",
                Status = QueuedMessageStatus.Pending,
                AcceptedUtc = acceptedUtc,
                CreatedUtc = acceptedUtc,
                NextAttemptAtUtc = acceptedUtc,
                ExpiresUtc = acceptedUtc.AddHours(1)
            };

            message.Recipients.Add(new QueuedMessageRecipient
            {
                RecipientAddress = "recipient@example.test"
            });

            dbContext.QueuedMessages.Add(message);
            await dbContext.SaveChangesAsync();
            return message.Id;
        }

        public async Task AddOperationalEventAsync(string message, DateTimeOffset occurredUtc)
        {
            await using var dbContext = _dbContextFactory.CreateDbContext();
            dbContext.OperationalEvents.Add(new OperationalEvent
            {
                OccurredUtc = occurredUtc,
                Message = message
            });

            await dbContext.SaveChangesAsync();
        }

        public QueueIndexModel CreateQueueModel()
        {
            return AttachPageContext(new QueueIndexModel(
                _dbContextFactory,
                new TestQueueService(),
                TestDatabaseConfiguration.Sqlite,
                NullLogger<QueueIndexModel>.Instance));
        }

        public LogsIndexModel CreateLogsModel()
        {
            return AttachPageContext(new LogsIndexModel(
                _dbContextFactory,
                TestDatabaseConfiguration.Sqlite,
                NullLogger<LogsIndexModel>.Instance));
        }

        public DashboardIndexModel CreateDashboardModel(SuspiciousLoginSummary? suspiciousLogins = null)
        {
            return AttachPageContext(new DashboardIndexModel(
                _dbContextFactory,
                new TestRelayConfigurationService(),
                new StaticRuntimeStatusService(),
                new TestDashboardMetricsService(),
                new TestAdminSecurityActivityService(suspiciousLogins),
                TestDatabaseConfiguration.Sqlite,
                NullLogger<DashboardIndexModel>.Instance));
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
        }

        private static TModel AttachPageContext<TModel>(TModel model)
            where TModel : PageModel
        {
            model.PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext()
            };

            return model;
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext()
        {
            return new ApplicationDbContext(options);
        }
    }

    private sealed class TestRelayConfigurationService : IRelayConfigurationService
    {
        public Task<RelayConfigurationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new RelayConfigurationSnapshot());
        }

        public Task<RelayConfigurationEditModel> GetEditModelAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task SaveAsync(RelayConfigurationEditModel model, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestDashboardMetricsService : IDashboardMetricsService
    {
        public Task<DashboardMetricsSnapshot> GetSnapshotAsync(
            RelayConfigurationSnapshot configuration,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new DashboardMetricsSnapshot());
        }
    }

    private sealed class TestAdminSecurityActivityService(SuspiciousLoginSummary? suspiciousLogins = null) : IAdminSecurityActivityService
    {
        public Task<AdminLoginActivitySummary> GetLoginActivityAsync(
            string? userName,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AdminLoginActivitySummary(userName, null, null, 0, 0));
        }

        public Task<SuspiciousLoginSummary> GetSuspiciousLoginSummaryAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(suspiciousLogins ?? SuspiciousLoginSummary.Empty);
        }
    }

    private sealed class TestQueueService : IMessageQueueService
    {
        public Task EnqueueAsync(NewQueuedMessageRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DeliveryWorkItem?> TryClaimNextAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkDeliveredAsync(DeliveryWorkItem workItem, DeliveryResult result, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkFailedAsync(DeliveryWorkItem workItem, DeliveryResult result, RelayConfigurationSnapshot configuration, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<QueueActionResult> RetryNowAsync(Guid messageId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<QueueBulkActionResult> RetryNowAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<QueueActionResult> PurgeAsync(Guid messageId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<QueueBulkActionResult> PurgeAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> CleanupAsync(RelayConfigurationSnapshot configuration, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
