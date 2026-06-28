using System.Buffers;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Services.Smtp;
using Relaywright.Web.Tests.Support;
using SmtpServer.Protocol;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class SmtpIntakeIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task SmtpDataAcceptedWritesSpoolAndQueueMetadataBeforeOk()
    {
        await using var database = await SqliteTestStore.CreateAsync(seedRelayConfiguration: true);
        using var appData = TempAppData.Create();
        var events = new RecordingOperationalEventService();
        var signal = new RecordingQueueSignal();
        var spool = new MessageSpoolService(appData.Paths, NullLogger<MessageSpoolService>.Instance);
        var queue = new MessageQueueService(
            database.DbContextFactory,
            new RetryDelayCalculator(),
            spool,
            events,
            signal,
            NullLogger<MessageQueueService>.Instance);
        var store = new RelayMessageStore(
            spool,
            queue,
            new StaticRelayConfigurationService(TestData.Snapshot()),
            events,
            NullLogger<RelayMessageStore>.Instance);
        var sessionId = Guid.NewGuid();
        var session = new FakeSessionContext(IPAddress.Parse("127.0.0.1"), sessionId);
        var transaction = new FakeMessageTransaction(
            recipients: ["one@example.test", "two@example.test", "one@example.test"]);
        var bytes = TestData.MimeBytes();

        var response = await store.SaveAsync(
            session,
            transaction,
            new ReadOnlySequence<byte>(bytes),
            CancellationToken.None);

        Assert.Equal(SmtpReplyCode.Ok, response.ReplyCode);
        Assert.Equal(1, signal.PulseCount);
        await using var dbContext = database.CreateDbContext();
        var queued = await dbContext.QueuedMessages
            .Include(x => x.Recipients)
            .SingleAsync();
        Assert.Equal(sessionId, queued.SessionId);
        Assert.Equal(QueuedMessageStatus.Pending, queued.Status);
        Assert.Equal("127.0.0.1", queued.RemoteIpAddress);
        Assert.Equal(bytes.Length, queued.MessageSizeBytes);
        Assert.Equal(["one@example.test", "two@example.test"], queued.Recipients.Select(x => x.RecipientAddress).Order());
        Assert.Equal(bytes, await File.ReadAllBytesAsync(spool.GetAbsolutePath(queued.SpoolFileRelativePath)));
        Assert.Contains(events.Events, x => x.Category == OperationalEventCategory.SmtpSession);
        Assert.Contains(events.Events, x => x.Category == OperationalEventCategory.Queue);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueueFailureAfterSpoolWriteDeletesOrphanAndReturnsTransactionFailed()
    {
        using var appData = TempAppData.Create();
        var events = new RecordingOperationalEventService();
        var spool = new MessageSpoolService(appData.Paths, NullLogger<MessageSpoolService>.Instance);
        var store = new RelayMessageStore(
            spool,
            new ThrowingQueueService(),
            new StaticRelayConfigurationService(TestData.Snapshot()),
            events,
            NullLogger<RelayMessageStore>.Instance);

        var response = await store.SaveAsync(
            new FakeSessionContext(IPAddress.Parse("127.0.0.1")),
            new FakeMessageTransaction(),
            new ReadOnlySequence<byte>(TestData.MimeBytes()),
            CancellationToken.None);

        Assert.Equal(SmtpReplyCode.TransactionFailed, response.ReplyCode);
        Assert.Empty(Directory.EnumerateFiles(appData.Paths.SpoolRootDirectory, "*.eml", SearchOption.AllDirectories));
        Assert.Contains(events.Events, x =>
            x.Severity == EventSeverity.Error
            && x.Category == OperationalEventCategory.Queue
            && x.Message.Contains("Failed to persist", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ThrowingQueueService : IMessageQueueService
    {
        public Task EnqueueAsync(NewQueuedMessageRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Simulated queue metadata failure.");
        }

        public Task<DeliveryWorkItem?> TryClaimNextAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkDeliveredAsync(DeliveryWorkItem workItem, DeliveryResult result, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkFailedAsync(DeliveryWorkItem workItem, DeliveryResult result, RelayConfigurationSnapshot configuration, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<QueueActionResult> RetryNowAsync(Guid messageId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<QueueActionResult> PurgeAsync(Guid messageId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> CleanupAsync(RelayConfigurationSnapshot configuration, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
