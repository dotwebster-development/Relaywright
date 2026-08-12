using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class MessageQueueServiceTests
{
    [Fact]
    public async Task TryClaimNextClaimsOnlyDueEligibleMessage()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        await fixture.AddMessageAsync(QueuedMessageStatus.Pending, nextAttemptAtUtc: now.AddHours(1));
        var due = await fixture.AddMessageAsync(QueuedMessageStatus.Pending, nextAttemptAtUtc: now.AddMinutes(-1));

        var claimed = await fixture.Service.TryClaimNextAsync(CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(due.Id, claimed!.MessageId);
        Assert.Equal(1, claimed.AttemptNumber);
        Assert.Equal(QueuedMessageStatus.InProgress, await fixture.GetStatusAsync(due.Id));
        Assert.Equal(1, await fixture.GetDeliveryAttemptCountAsync(due.Id));
    }

    [Fact]
    public async Task TryClaimNextDoesNotImmediatelyDoubleClaim()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        var message = await fixture.AddMessageAsync(QueuedMessageStatus.Pending);

        var firstClaim = await fixture.Service.TryClaimNextAsync(CancellationToken.None);
        var secondClaim = await fixture.Service.TryClaimNextAsync(CancellationToken.None);

        Assert.NotNull(firstClaim);
        Assert.Equal(message.Id, firstClaim!.MessageId);
        Assert.Null(secondClaim);
        Assert.Equal(1, await fixture.GetDeliveryAttemptCountAsync(message.Id));
    }

    [Fact]
    public async Task TryClaimNextRecoversStaleInProgressMessage()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        var message = await fixture.AddMessageAsync(
            QueuedMessageStatus.InProgress,
            attemptCount: 2,
            lastAttemptStartedUtc: DateTimeOffset.UtcNow.AddMinutes(-20));

        var claimed = await fixture.Service.TryClaimNextAsync(CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(message.Id, claimed!.MessageId);
        Assert.Equal(3, claimed.AttemptNumber);
        Assert.Equal(3, (await fixture.FindAsync(message.Id))!.AttemptCount);
        Assert.Equal(1, await fixture.GetDeliveryAttemptCountAsync(message.Id));
    }

    [Fact]
    public async Task ConfiguredStaleClaimThresholdControlsRecovery()
    {
        var now = new DateTimeOffset(2032, 4, 5, 6, 7, 8, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);
        await using var fixture = await QueueFixture.CreateAsync(
            timeProvider,
            new QueueProcessingOptions { StaleClaimMinutes = 30 });
        var message = await fixture.AddMessageAsync(
            QueuedMessageStatus.InProgress,
            attemptCount: 1,
            lastAttemptStartedUtc: now.AddMinutes(-20),
            expiresUtc: now.AddHours(1));

        Assert.Null(await fixture.Service.TryClaimNextAsync(CancellationToken.None));

        timeProvider.SetUtcNow(now.AddMinutes(11));
        var claimed = await fixture.Service.TryClaimNextAsync(CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(message.Id, claimed!.MessageId);
        Assert.Equal(2, claimed.AttemptNumber);
    }

    [Fact]
    public async Task QueueTimestampsUseInjectedTimeProvider()
    {
        var now = new DateTimeOffset(2032, 4, 5, 6, 7, 8, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);
        await using var fixture = await QueueFixture.CreateAsync(timeProvider);
        await fixture.AddMessageAsync(
            QueuedMessageStatus.Pending,
            acceptedUtc: now.AddMinutes(-1),
            nextAttemptAtUtc: now.AddMinutes(-1),
            expiresUtc: now.AddHours(1));

        var workItem = await fixture.Service.TryClaimNextAsync(CancellationToken.None);
        Assert.NotNull(workItem);

        await fixture.Service.MarkFailedAsync(
            workItem!,
            new DeliveryResult
            {
                FailureCategory = DeliveryFailureCategory.Transient,
                ErrorDetail = "temporary failure"
            },
            new RelayConfigurationSnapshot
            {
                MaxRetryCount = 3,
                InitialRetryDelaySeconds = 60,
                MaxRetryDelaySeconds = 300
            },
            CancellationToken.None);

        var saved = await fixture.FindAsync(workItem!.MessageId);
        Assert.NotNull(saved);
        Assert.Equal(now, saved!.LastAttemptStartedUtc);
        Assert.Equal(now, saved.LastAttemptCompletedUtc);
        Assert.Equal(now.AddMinutes(1), saved.NextAttemptAtUtc);
    }

    [Fact]
    public async Task ConcurrentStatusMutationIsRejected()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        var message = await fixture.AddMessageAsync(QueuedMessageStatus.Failed);
        await using var firstContext = fixture.CreateDbContext();
        await using var secondContext = fixture.CreateDbContext();
        var firstCopy = await firstContext.QueuedMessages.SingleAsync(x => x.Id == message.Id);
        var secondCopy = await secondContext.QueuedMessages.SingleAsync(x => x.Id == message.Id);

        firstCopy.Status = QueuedMessageStatus.RetryScheduled;
        firstCopy.NextAttemptAtUtc = DateTimeOffset.UtcNow;
        await firstContext.SaveChangesAsync();

        secondCopy.Status = QueuedMessageStatus.RetryScheduled;
        secondCopy.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddMinutes(1);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
    }

    [Fact]
    public async Task RepeatingDeliveredCompletionForSameAttemptIsIdempotent()
    {
        var now = new DateTimeOffset(2032, 4, 5, 6, 7, 8, TimeSpan.Zero);
        await using var fixture = await QueueFixture.CreateAsync(new FixedTimeProvider(now));
        await fixture.AddMessageAsync(
            QueuedMessageStatus.Pending,
            acceptedUtc: now.AddMinutes(-1),
            nextAttemptAtUtc: now.AddMinutes(-1),
            expiresUtc: now.AddHours(1));
        var workItem = await fixture.Service.TryClaimNextAsync(CancellationToken.None);
        Assert.NotNull(workItem);
        var result = new DeliveryResult
        {
            Succeeded = true,
            ResponseCode = "250",
            ResponseText = "queued"
        };

        await fixture.Service.MarkDeliveredAsync(workItem!, result, CancellationToken.None);
        await fixture.Service.MarkDeliveredAsync(workItem!, result, CancellationToken.None);

        var saved = await fixture.FindAsync(workItem!.MessageId);
        Assert.NotNull(saved);
        Assert.Equal(QueuedMessageStatus.Delivered, saved!.Status);
        Assert.Single(saved.DeliveryAttempts);
        Assert.True(saved.DeliveryAttempts.Single().Succeeded);
        Assert.Equal(now, saved.DeliveredUtc);
    }

    [Fact]
    public async Task RetryNowRejectsDeliveredMessage()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        var message = await fixture.AddMessageAsync(QueuedMessageStatus.Delivered);

        var result = await fixture.OperatorService.RetryNowAsync(message.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Delivered messages cannot be retried.", result.Message);
        Assert.Equal(QueuedMessageStatus.Delivered, await fixture.GetStatusAsync(message.Id));
    }

    [Fact]
    public async Task PurgeRejectsInProgressMessage()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        var message = await fixture.AddMessageAsync(QueuedMessageStatus.InProgress);

        var result = await fixture.OperatorService.PurgeAsync(message.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Message is currently being delivered and cannot be purged.", result.Message);
        Assert.Equal(QueuedMessageStatus.InProgress, await fixture.GetStatusAsync(message.Id));
        Assert.Empty(fixture.SpoolService.DeletedPaths);
    }

    [Fact]
    public async Task PurgeRemovesTerminalMessageAndSpoolFile()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        var message = await fixture.AddMessageAsync(QueuedMessageStatus.Failed);

        var result = await fixture.OperatorService.PurgeAsync(message.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Queued message purged.", result.Message);
        Assert.Null(await fixture.FindAsync(message.Id));
        Assert.Contains(message.SpoolFileRelativePath, fixture.SpoolService.DeletedPaths);
    }

    [Fact]
    public async Task BulkRetrySummarizesSucceededRejectedAndMissingMessages()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        var failed = await fixture.AddMessageAsync(QueuedMessageStatus.Failed);
        var delivered = await fixture.AddMessageAsync(QueuedMessageStatus.Delivered);
        var missing = Guid.NewGuid();

        var result = await fixture.OperatorService.RetryNowAsync([failed.Id, delivered.Id, missing], CancellationToken.None);

        Assert.Equal(3, result.Requested);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Rejected);
        Assert.Equal(1, result.Missing);
        Assert.Equal(QueuedMessageStatus.RetryScheduled, await fixture.GetStatusAsync(failed.Id));
        Assert.Equal(QueuedMessageStatus.Delivered, await fixture.GetStatusAsync(delivered.Id));
    }

    [Fact]
    public async Task BulkPurgeCountsSpoolDeleteFailures()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        var failed = await fixture.AddMessageAsync(QueuedMessageStatus.Failed);
        fixture.SpoolService.ThrowOnDelete = true;

        var result = await fixture.OperatorService.PurgeAsync([failed.Id], CancellationToken.None);

        Assert.Equal(1, result.Requested);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.SpoolDeleteFailures);
        Assert.Null(await fixture.FindAsync(failed.Id));
    }

    [Fact]
    public async Task CleanupOnlyRemovesRetentionEligibleRecordsAndSpoolFiles()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var deliveredOld = await fixture.AddMessageAsync(
            QueuedMessageStatus.Delivered,
            deliveredUtc: now.AddHours(-2),
            lastAttemptCompletedUtc: now.AddHours(-2));
        var deliveredFresh = await fixture.AddMessageAsync(
            QueuedMessageStatus.Delivered,
            deliveredUtc: now,
            lastAttemptCompletedUtc: now);
        var failedOld = await fixture.AddMessageAsync(
            QueuedMessageStatus.Failed,
            lastAttemptCompletedUtc: now.AddHours(-2));
        var failedFresh = await fixture.AddMessageAsync(
            QueuedMessageStatus.Failed,
            lastAttemptCompletedUtc: now);
        var expiredActive = await fixture.AddMessageAsync(
            QueuedMessageStatus.Pending,
            expiresUtc: now.AddMinutes(-1));
        await fixture.AddOperationalEventAsync(now.AddHours(-2));
        await fixture.AddOperationalEventAsync(now);

        var deleted = await fixture.MaintenanceService.CleanupAsync(new RelayConfigurationSnapshot
        {
            DeliveredRetentionHours = 1,
            FailedRetentionHours = 1,
            EventRetentionHours = 1
        }, CancellationToken.None);

        Assert.Equal(3, deleted);
        Assert.Null(await fixture.FindAsync(deliveredOld.Id));
        Assert.Null(await fixture.FindAsync(failedOld.Id));
        Assert.NotNull(await fixture.FindAsync(deliveredFresh.Id));
        Assert.NotNull(await fixture.FindAsync(failedFresh.Id));
        Assert.Equal(QueuedMessageStatus.Expired, await fixture.GetStatusAsync(expiredActive.Id));
        Assert.Contains(deliveredOld.SpoolFileRelativePath, fixture.SpoolService.DeletedPaths);
        Assert.Contains(failedOld.SpoolFileRelativePath, fixture.SpoolService.DeletedPaths);
        Assert.DoesNotContain(expiredActive.SpoolFileRelativePath, fixture.SpoolService.DeletedPaths);
        Assert.Equal(1, await fixture.GetOperationalEventCountAsync());
    }

    private sealed class QueueFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly TestDbContextFactory _dbContextFactory;

        private QueueFixture(
            SqliteConnection connection,
            TestDbContextFactory dbContextFactory,
            TimeProvider timeProvider,
            QueueProcessingOptions? queueOptions)
        {
            _connection = connection;
            _dbContextFactory = dbContextFactory;
            SpoolService = new TestSpoolService();
            Service = TestMessageQueueServiceFactory.Create(
                dbContextFactory,
                new TestOperationalEventService(),
                new TestQueueSignal(),
                TestDatabaseConfiguration.Sqlite,
                timeProvider,
                queueOptions);
            OperatorService = new QueueOperatorService(
                dbContextFactory,
                SpoolService,
                new ImmediateBackupCoordinator(),
                new TestOperationalEventService(),
                new TestQueueSignal(),
                NullLogger<QueueOperatorService>.Instance,
                timeProvider);
            MaintenanceService = new QueueMaintenanceService(
                dbContextFactory,
                SpoolService,
                new ImmediateBackupCoordinator(),
                TestDatabaseConfiguration.Sqlite,
                NullLogger<QueueMaintenanceService>.Instance,
                timeProvider);
        }

        public MessageQueueService Service { get; }

        public QueueOperatorService OperatorService { get; }

        public QueueMaintenanceService MaintenanceService { get; }

        public TestSpoolService SpoolService { get; }

        public static async Task<QueueFixture> CreateAsync(
            TimeProvider? timeProvider = null,
            QueueProcessingOptions? queueOptions = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var factory = new TestDbContextFactory(options);
            await using var dbContext = factory.CreateDbContext();
            await dbContext.Database.EnsureCreatedAsync();

            return new QueueFixture(
                connection,
                factory,
                timeProvider ?? TimeProvider.System,
                queueOptions);
        }

        public async Task<QueuedMessage> AddMessageAsync(
            QueuedMessageStatus status,
            DateTimeOffset? acceptedUtc = null,
            DateTimeOffset? nextAttemptAtUtc = null,
            DateTimeOffset? lastAttemptStartedUtc = null,
            DateTimeOffset? lastAttemptCompletedUtc = null,
            DateTimeOffset? deliveredUtc = null,
            DateTimeOffset? expiresUtc = null,
            int attemptCount = 0)
        {
            var accepted = acceptedUtc ?? DateTimeOffset.UtcNow;
            await using var dbContext = _dbContextFactory.CreateDbContext();
            var message = new QueuedMessage
            {
                Id = Guid.NewGuid(),
                SessionId = Guid.NewGuid(),
                EnvelopeFrom = "sender@example.test",
                SpoolFileRelativePath = $"{Guid.NewGuid():N}.eml",
                Status = status,
                AttemptCount = attemptCount,
                AcceptedUtc = accepted,
                CreatedUtc = accepted,
                NextAttemptAtUtc = nextAttemptAtUtc ?? accepted,
                ExpiresUtc = expiresUtc ?? accepted.AddHours(1),
                LastAttemptStartedUtc = lastAttemptStartedUtc,
                LastAttemptCompletedUtc = lastAttemptCompletedUtc
                    ?? (status is QueuedMessageStatus.Failed or QueuedMessageStatus.Expired
                        ? accepted
                        : null),
                DeliveredUtc = deliveredUtc
            };

            message.Recipients.Add(new QueuedMessageRecipient
            {
                RecipientAddress = "recipient@example.test"
            });

            dbContext.QueuedMessages.Add(message);
            await dbContext.SaveChangesAsync();
            return message;
        }

        public async Task AddOperationalEventAsync(DateTimeOffset occurredUtc)
        {
            await using var dbContext = _dbContextFactory.CreateDbContext();
            dbContext.OperationalEvents.Add(new OperationalEvent
            {
                OccurredUtc = occurredUtc,
                Message = "event"
            });
            await dbContext.SaveChangesAsync();
        }

        public async Task<QueuedMessage?> FindAsync(Guid id)
        {
            await using var dbContext = _dbContextFactory.CreateDbContext();
            return await dbContext.QueuedMessages
                .Include(x => x.DeliveryAttempts)
                .SingleOrDefaultAsync(x => x.Id == id);
        }

        public ApplicationDbContext CreateDbContext()
        {
            return _dbContextFactory.CreateDbContext();
        }

        public async Task<QueuedMessageStatus?> GetStatusAsync(Guid id)
        {
            return (await FindAsync(id))?.Status;
        }

        public async Task<int> GetDeliveryAttemptCountAsync(Guid id)
        {
            await using var dbContext = _dbContextFactory.CreateDbContext();
            return await dbContext.DeliveryAttempts.CountAsync(x => x.QueuedMessageId == id);
        }

        public async Task<int> GetOperationalEventCountAsync()
        {
            await using var dbContext = _dbContextFactory.CreateDbContext();
            return await dbContext.OperationalEvents.CountAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
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

    private sealed class TestSpoolService : IMessageSpoolService
    {
        public List<string> DeletedPaths { get; } = new();

        public bool ThrowOnDelete { get; set; }

        public Task<string> WriteAsync(Guid messageId, DateTimeOffset acceptedUtc, System.Buffers.ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
        {
            return Task.FromResult($"{messageId:N}.eml");
        }

        public Stream OpenRead(string relativePath)
        {
            throw new NotSupportedException();
        }

        public string GetAbsolutePath(string relativePath)
        {
            return relativePath;
        }

        public bool Exists(string relativePath)
        {
            return true;
        }

        public Task DeleteIfExistsAsync(string relativePath, CancellationToken cancellationToken)
        {
            if (ThrowOnDelete)
            {
                throw new IOException("Simulated delete failure.");
            }

            DeletedPaths.Add(relativePath);
            return Task.CompletedTask;
        }
    }

    private sealed class TestOperationalEventService : IOperationalEventService
    {
        public Task WriteAsync(OperationalEventRequest request, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestQueueSignal : IQueueSignal
    {
        public void Pulse()
        {
        }

        public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
