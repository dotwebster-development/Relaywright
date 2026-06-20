using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class MessageQueueServiceTests
{
    [Fact]
    public async Task RetryNowRejectsDeliveredMessage()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        var message = await fixture.AddMessageAsync(QueuedMessageStatus.Delivered);

        var result = await fixture.Service.RetryNowAsync(message.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Delivered messages cannot be retried.", result.Message);
        Assert.Equal(QueuedMessageStatus.Delivered, await fixture.GetStatusAsync(message.Id));
    }

    [Fact]
    public async Task PurgeRejectsInProgressMessage()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        var message = await fixture.AddMessageAsync(QueuedMessageStatus.InProgress);

        var result = await fixture.Service.PurgeAsync(message.Id, CancellationToken.None);

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

        var result = await fixture.Service.PurgeAsync(message.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Queued message purged.", result.Message);
        Assert.Null(await fixture.FindAsync(message.Id));
        Assert.Contains(message.SpoolFileRelativePath, fixture.SpoolService.DeletedPaths);
    }

    private sealed class QueueFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly TestDbContextFactory _dbContextFactory;

        private QueueFixture(SqliteConnection connection, TestDbContextFactory dbContextFactory)
        {
            _connection = connection;
            _dbContextFactory = dbContextFactory;
            SpoolService = new TestSpoolService();
            Service = new MessageQueueService(
                dbContextFactory,
                new RetryDelayCalculator(),
                SpoolService,
                new TestOperationalEventService(),
                new TestQueueSignal(),
                NullLogger<MessageQueueService>.Instance);
        }

        public MessageQueueService Service { get; }

        public TestSpoolService SpoolService { get; }

        public static async Task<QueueFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var factory = new TestDbContextFactory(options);
            await using var dbContext = factory.CreateDbContext();
            await dbContext.Database.EnsureCreatedAsync();

            return new QueueFixture(connection, factory);
        }

        public async Task<QueuedMessage> AddMessageAsync(QueuedMessageStatus status)
        {
            await using var dbContext = _dbContextFactory.CreateDbContext();
            var message = new QueuedMessage
            {
                Id = Guid.NewGuid(),
                SessionId = Guid.NewGuid(),
                EnvelopeFrom = "sender@example.test",
                SpoolFileRelativePath = $"{Guid.NewGuid():N}.eml",
                Status = status,
                AcceptedUtc = DateTimeOffset.UtcNow,
                CreatedUtc = DateTimeOffset.UtcNow,
                NextAttemptAtUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
                LastAttemptCompletedUtc = status is QueuedMessageStatus.Failed or QueuedMessageStatus.Expired
                    ? DateTimeOffset.UtcNow
                    : null
            };

            message.Recipients.Add(new QueuedMessageRecipient
            {
                RecipientAddress = "recipient@example.test"
            });

            dbContext.QueuedMessages.Add(message);
            await dbContext.SaveChangesAsync();
            return message;
        }

        public async Task<QueuedMessage?> FindAsync(Guid id)
        {
            await using var dbContext = _dbContextFactory.CreateDbContext();
            return await dbContext.QueuedMessages.SingleOrDefaultAsync(x => x.Id == id);
        }

        public async Task<QueuedMessageStatus?> GetStatusAsync(Guid id)
        {
            return (await FindAsync(id))?.Status;
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
