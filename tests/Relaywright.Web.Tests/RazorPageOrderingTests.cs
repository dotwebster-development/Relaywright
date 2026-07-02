using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Alerts;
using Relaywright.Web.Services.Backups;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Tests.Support;
using Xunit;

using BackupsModel = Relaywright.Web.Pages.Operations.BackupsModel;
using DashboardIndexModel = Relaywright.Web.Pages.IndexModel;
using LogsIndexModel = Relaywright.Web.Pages.Logs.IndexModel;
using MessageDetailsModel = Relaywright.Web.Pages.Messages.DetailsModel;
using QueueIndexModel = Relaywright.Web.Pages.Queue.IndexModel;
using TrustedNetworksModel = Relaywright.Web.Pages.Settings.TrustedNetworksModel;

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
    public async Task QueuePageBuildsFailureGroups()
    {
        await using var fixture = await PageFixture.CreateAsync();
        await fixture.AddQueuedMessageAsync(
            DateTimeOffset.UtcNow.AddMinutes(-20),
            QueuedMessageStatus.Failed,
            DeliveryFailureCategory.Configuration);
        await fixture.AddQueuedMessageAsync(
            DateTimeOffset.UtcNow.AddMinutes(-10),
            QueuedMessageStatus.Expired,
            DeliveryFailureCategory.Configuration);
        await fixture.AddQueuedMessageAsync(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            QueuedMessageStatus.Failed,
            DeliveryFailureCategory.Permanent);
        var model = fixture.CreateQueueModel();

        await model.OnGetAsync("failed", CancellationToken.None);

        Assert.Equal(2, model.FailureGroups.Count);
        var configuration = model.FailureGroups.Single(x => x.FailureCategory == DeliveryFailureCategory.Configuration);
        Assert.Equal(2, configuration.Count);
    }

    [Fact]
    public async Task QueuePageBuildsFailurePreviewFromResponseBeforeFallbackError()
    {
        await using var fixture = await PageFixture.CreateAsync();
        var id = await fixture.AddQueuedMessageAsync(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            QueuedMessageStatus.RetryScheduled,
            DeliveryFailureCategory.Transient,
            lastResponseCode: "421",
            lastResponseText: "Temporary upstream throttling response with a long diagnostic payload",
            lastError: "Fallback exception text");
        var model = fixture.CreateQueueModel();

        await model.OnGetAsync("active", CancellationToken.None);

        var message = Assert.Single(model.Messages, x => x.Id == id);
        var preview = QueueIndexModel.BuildFailurePreview(message, maxLength: 32);
        Assert.True(preview.HasDetail);
        Assert.Equal(DeliveryFailureCategory.Transient, preview.FailureCategory);
        Assert.Equal("421", preview.ResponseCode);
        Assert.True(preview.IsTruncated);
        Assert.DoesNotContain("Fallback", preview.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MessageDetailsLoadsRelatedEventsByMessageAndSession()
    {
        await using var fixture = await PageFixture.CreateAsync();
        var sessionId = Guid.NewGuid();
        var messageId = await fixture.AddQueuedMessageAsync(DateTimeOffset.UtcNow.AddMinutes(-5), sessionId: sessionId);
        await fixture.AddOperationalEventAsync(
            "message event",
            DateTimeOffset.UtcNow.AddMinutes(-2),
            queuedMessageId: messageId);
        await fixture.AddOperationalEventAsync(
            "session event",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            sessionId: sessionId);
        await fixture.AddOperationalEventAsync("unrelated", DateTimeOffset.UtcNow);
        var model = fixture.CreateMessageDetailsModel();

        _ = await model.OnGetAsync(messageId, CancellationToken.None);

        Assert.NotNull(model.Message);
        Assert.Equal(["session event", "message event"], model.RelatedEvents.Select(x => x.Message));
    }

    [Fact]
    public async Task LogsPageBuildsSummaryFromCurrentFilters()
    {
        await using var fixture = await PageFixture.CreateAsync();
        await fixture.AddOperationalEventAsync(
            "policy accepted",
            DateTimeOffset.UtcNow.AddMinutes(-3),
            EventSeverity.Information,
            OperationalEventCategory.Security);
        await fixture.AddOperationalEventAsync(
            "policy rejected",
            DateTimeOffset.UtcNow.AddMinutes(-2),
            EventSeverity.Warning,
            OperationalEventCategory.Security);
        await fixture.AddOperationalEventAsync(
            "delivery failed",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            EventSeverity.Error,
            OperationalEventCategory.Delivery);
        var model = fixture.CreateLogsModel();
        model.Category = OperationalEventCategory.Security;
        model.Search = "policy";

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(2, model.Summary.TotalCount);
        Assert.Equal(1, model.Summary.InformationCount);
        Assert.Equal(1, model.Summary.WarningCount);
        Assert.Equal(0, model.Summary.ErrorCount);
        var category = Assert.Single(model.Summary.CategoryCounts);
        Assert.Equal(OperationalEventCategory.Security, category.Category);
        Assert.Equal(2, category.Count);
    }

    [Fact]
    public async Task LogsPageFiltersBySessionIdWithSqlite()
    {
        await using var fixture = await PageFixture.CreateAsync();
        var sessionId = Guid.NewGuid();
        await fixture.AddOperationalEventAsync("matching session", DateTimeOffset.UtcNow.AddMinutes(-1), sessionId: sessionId);
        await fixture.AddOperationalEventAsync("other session", DateTimeOffset.UtcNow, sessionId: Guid.NewGuid());
        var model = fixture.CreateLogsModel();
        model.SessionId = sessionId;

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(1, model.TotalCount);
        Assert.Equal("matching session", Assert.Single(model.Events).Message);
    }

    [Fact]
    public void TrustedNetworkActivitySummariesMatchRemoteIpToCidr()
    {
        var now = DateTimeOffset.UtcNow;
        var summaries = TrustedNetworksModel.BuildActivitySummaries(
            [
                new TrustedNetwork { Id = 1, Cidr = "10.10.0.0/16", Description = "active" },
                new TrustedNetwork { Id = 2, Cidr = "192.0.2.0/24", Description = "stale" },
                new TrustedNetwork { Id = 3, Cidr = "203.0.113.0/24", Description = "unused" }
            ],
            [
                new Relaywright.Web.Pages.Settings.TrustedNetworkActivityEvent(
                    now.AddMinutes(-5),
                    OperationalEventCategory.Security,
                    "10.10.1.5",
                    "SMTP MAIL FROM accepted from trusted device profile."),
                new Relaywright.Web.Pages.Settings.TrustedNetworkActivityEvent(
                    now.AddDays(-40),
                    OperationalEventCategory.Security,
                    "192.0.2.10",
                    "SMTP MAIL FROM denied by submission policy.")
            ],
            now);

        Assert.Equal("Active", summaries[1].StatusLabel);
        Assert.Contains("Accepted", summaries[1].LastDecision);
        Assert.Equal("Stale", summaries[2].StatusLabel);
        Assert.Contains("Rejected", summaries[2].LastDecision);
        Assert.Equal("Unused", summaries[3].StatusLabel);
    }

    [Fact]
    public void BackupScheduleVisibilityShowsNextRunAndRetentionPreview()
    {
        var now = DateTimeOffset.UtcNow;
        var runs = new[]
        {
            new BackupRun
            {
                Id = Guid.NewGuid(),
                StartedUtc = now.AddHours(-1),
                Status = BackupRunStatus.Succeeded,
                LastValidatedUtc = now.AddMinutes(-30),
                LastValidationSucceeded = true
            },
            new BackupRun { Id = Guid.NewGuid(), StartedUtc = now.AddHours(-2), Status = BackupRunStatus.Succeeded },
            new BackupRun { Id = Guid.NewGuid(), StartedUtc = now.AddHours(-3), Status = BackupRunStatus.Succeeded }
        };

        var visibility = BackupsModel.BuildScheduleVisibility(
            new BackupScheduleState
            {
                IsEnabled = true,
                IntervalHours = 2,
                RetentionCount = 2,
                LastRunUtc = now.AddHours(-1)
            },
            runs,
            new BackupReadiness { LastGoodBackupAgeHours = 1 },
            now);

        Assert.Equal(now.AddHours(1), visibility.NextRunUtc);
        Assert.Equal(3, visibility.SuccessfulBackupCount);
        Assert.Equal(2, visibility.RetainedBackupCount);
        Assert.Equal(1, visibility.PrunableSucceededBackupCount);
        Assert.Equal("1 hour(s)", visibility.ValidationAge);
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

        public async Task<Guid> AddQueuedMessageAsync(
            DateTimeOffset acceptedUtc,
            QueuedMessageStatus status = QueuedMessageStatus.Pending,
            DeliveryFailureCategory failureCategory = DeliveryFailureCategory.None,
            Guid? sessionId = null,
            string? remoteIpAddress = null,
            string? lastResponseCode = null,
            string? lastResponseText = null,
            string? lastError = null)
        {
            await using var dbContext = _dbContextFactory.CreateDbContext();
            var message = new QueuedMessage
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId ?? Guid.NewGuid(),
                RemoteIpAddress = remoteIpAddress,
                EnvelopeFrom = "sender@example.test",
                SpoolFileRelativePath = $"{Guid.NewGuid():N}.eml",
                Status = status,
                FailureCategory = failureCategory,
                LastResponseCode = lastResponseCode,
                LastResponseText = lastResponseText,
                LastError = lastError,
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

        public async Task AddOperationalEventAsync(
            string message,
            DateTimeOffset occurredUtc,
            EventSeverity severity = EventSeverity.Information,
            OperationalEventCategory category = OperationalEventCategory.System,
            Guid? sessionId = null,
            Guid? queuedMessageId = null,
            string? remoteIpAddress = null,
            string? detail = null)
        {
            await using var dbContext = _dbContextFactory.CreateDbContext();
            dbContext.OperationalEvents.Add(new OperationalEvent
            {
                OccurredUtc = occurredUtc,
                Severity = severity,
                Category = category,
                SessionId = sessionId,
                QueuedMessageId = queuedMessageId,
                RemoteIpAddress = remoteIpAddress,
                Message = message,
                Detail = detail
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

        public MessageDetailsModel CreateMessageDetailsModel()
        {
            return AttachPageContext(new MessageDetailsModel(
                _dbContextFactory,
                new TestQueueService(),
                new NullMetadataService(),
                NullLogger<MessageDetailsModel>.Instance));
        }

        public DashboardIndexModel CreateDashboardModel()
        {
            return AttachPageContext(new DashboardIndexModel(
                _dbContextFactory,
                new TestRelayConfigurationService(),
                new StaticRuntimeStatusService(),
                new TestDashboardMetricsService(),
                TestDatabaseConfiguration.Sqlite,
                new TestAlertService(),
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

    private sealed class TestAlertService : IAlertService
    {
        public Task<IReadOnlyList<AlertRule>> GetRulesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<AlertRule>>(Array.Empty<AlertRule>());
        }

        public Task<IReadOnlyList<AlertRuleSummary>> GetRuleSummariesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<AlertRuleSummary>>(Array.Empty<AlertRuleSummary>());
        }

        public Task<IReadOnlyList<AlertResult>> GetRecentResultsAsync(int count, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<AlertResult>>(Array.Empty<AlertResult>());
        }

        public Task SaveRuleAsync(AlertRule rule, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task EvaluateAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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

    private sealed class NullMetadataService : IMessageMetadataService
    {
        public Task<MessageMetadataSummary?> ReadAsync(string relativePath, CancellationToken cancellationToken)
        {
            return Task.FromResult<MessageMetadataSummary?>(null);
        }
    }
}
