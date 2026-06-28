using System.Buffers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class QueueLifecycleIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueueLifecycleCanClaimAndMarkDeliveredUsingRealSqliteAndSpool()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        using var appData = TempAppData.Create();
        var events = new RecordingOperationalEventService();
        var signal = new RecordingQueueSignal();
        var spool = new MessageSpoolService(appData.Paths, NullLogger<MessageSpoolService>.Instance);
        var service = new MessageQueueService(
            database.DbContextFactory,
            new RetryDelayCalculator(),
            spool,
            new ImmediateBackupCoordinator(),
            events,
            signal,
            NullLogger<MessageQueueService>.Instance);
        var bytes = TestData.MimeBytes();
        var messageId = Guid.NewGuid();
        var acceptedUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        var spoolPath = await spool.WriteAsync(messageId, acceptedUtc, new ReadOnlySequence<byte>(bytes), CancellationToken.None);

        await service.EnqueueAsync(new NewQueuedMessageRequest
        {
            MessageId = messageId,
            SessionId = Guid.NewGuid(),
            RemoteIpAddress = "127.0.0.1",
            EnvelopeFrom = "sender@example.test",
            Recipients = ["recipient@example.test"],
            SpoolFileRelativePath = spoolPath,
            MessageSizeBytes = bytes.Length,
            AcceptedUtc = acceptedUtc,
            MessageExpirationHours = 24
        }, CancellationToken.None);
        var workItem = await service.TryClaimNextAsync(CancellationToken.None);
        Assert.NotNull(workItem);

        await service.MarkDeliveredAsync(workItem!, new DeliveryResult
        {
            Succeeded = true,
            ResponseCode = "250",
            ResponseText = "queued"
        }, CancellationToken.None);

        var saved = await database.FindMessageAsync(messageId);
        Assert.NotNull(saved);
        Assert.Equal(QueuedMessageStatus.Delivered, saved!.Status);
        Assert.Equal(1, saved.AttemptCount);
        Assert.Single(saved.DeliveryAttempts);
        Assert.True(saved.DeliveryAttempts.Single().Succeeded);
        Assert.True(spool.Exists(spoolPath));
        Assert.Equal(1, signal.PulseCount);
        Assert.Contains(events.Events, x => x.Category == OperationalEventCategory.Queue);
        Assert.Contains(events.Events, x => x.Category == OperationalEventCategory.Delivery);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueueLifecycleSchedulesRetryForTransientFailure()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        using var appData = TempAppData.Create();
        var service = new MessageQueueService(
            database.DbContextFactory,
            new RetryDelayCalculator(),
            new MessageSpoolService(appData.Paths, NullLogger<MessageSpoolService>.Instance),
            new ImmediateBackupCoordinator(),
            new RecordingOperationalEventService(),
            new RecordingQueueSignal(),
            NullLogger<MessageQueueService>.Instance);
        var messageId = Guid.NewGuid();
        var acceptedUtc = DateTimeOffset.UtcNow.AddMinutes(-1);

        await service.EnqueueAsync(new NewQueuedMessageRequest
        {
            MessageId = messageId,
            SessionId = Guid.NewGuid(),
            EnvelopeFrom = "sender@example.test",
            Recipients = ["recipient@example.test"],
            SpoolFileRelativePath = "message.eml",
            MessageSizeBytes = 100,
            AcceptedUtc = acceptedUtc,
            MessageExpirationHours = 24
        }, CancellationToken.None);
        var workItem = await service.TryClaimNextAsync(CancellationToken.None);
        Assert.NotNull(workItem);

        await service.MarkFailedAsync(workItem!, new DeliveryResult
        {
            Succeeded = false,
            FailureCategory = DeliveryFailureCategory.Transient,
            ErrorDetail = "temporary upstream failure"
        }, TestData.Snapshot(), CancellationToken.None);

        var saved = await database.FindMessageAsync(messageId);
        Assert.NotNull(saved);
        Assert.Equal(QueuedMessageStatus.RetryScheduled, saved!.Status);
        Assert.Equal(DeliveryFailureCategory.Transient, saved.FailureCategory);
        Assert.True(saved.NextAttemptAtUtc > saved.LastAttemptCompletedUtc);
        Assert.False(saved.DeliveryAttempts.Single().Succeeded);
    }
}
