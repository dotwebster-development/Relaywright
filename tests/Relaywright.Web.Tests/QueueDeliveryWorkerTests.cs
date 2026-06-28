using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Delivery;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Relay;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class QueueDeliveryWorkerTests
{
    [Fact]
    public async Task UnexpectedDeliveryExceptionIsMarkedFailedForRetry()
    {
        var queueService = new TestQueueService();
        var upstreamDelivery = new TestUpstreamDeliveryService
        {
            Exception = new InvalidOperationException("Unexpected local failure.")
        };
        var worker = CreateWorker(queueService, upstreamDelivery, new TestOperationalEventService());
        var workItem = CreateWorkItem();

        await ProcessWorkItemAsync(worker, workItem, new RelayConfigurationSnapshot());

        Assert.Equal(1, queueService.MarkFailedCallCount);
        Assert.Equal(DeliveryFailureCategory.Transient, queueService.LastFailureResult!.FailureCategory);
        Assert.Equal(nameof(InvalidOperationException), queueService.LastFailureResult.ExceptionType);
        Assert.Equal(0, queueService.MarkDeliveredCallCount);
    }

    [Fact]
    public async Task DeliveredStateWriteFailureDoesNotMarkMessageFailed()
    {
        var queueService = new TestQueueService
        {
            MarkDeliveredException = new InvalidOperationException("Database is locked.")
        };
        var events = new TestOperationalEventService();
        var upstreamDelivery = new TestUpstreamDeliveryService
        {
            Result = new DeliveryResult
            {
                Succeeded = true,
                ResponseText = "250 queued"
            }
        };
        var worker = CreateWorker(queueService, upstreamDelivery, events);
        var workItem = CreateWorkItem();

        await ProcessWorkItemAsync(worker, workItem, new RelayConfigurationSnapshot());

        Assert.Equal(3, queueService.MarkDeliveredCallCount);
        Assert.Equal(0, queueService.MarkFailedCallCount);
        Assert.Contains(events.Events, x =>
            x.Severity == EventSeverity.Error
            && x.Message.Contains("accepted upstream", StringComparison.OrdinalIgnoreCase));
    }

    private static QueueDeliveryWorker CreateWorker(
        IMessageQueueService queueService,
        IUpstreamDeliveryService upstreamDeliveryService,
        IOperationalEventService eventService)
    {
        return new QueueDeliveryWorker(
            new TestRelayConfigurationService(),
            queueService,
            upstreamDeliveryService,
            new TestQueueSignal(),
            eventService,
            NullLogger<QueueDeliveryWorker>.Instance);
    }

    private static DeliveryWorkItem CreateWorkItem()
    {
        return new DeliveryWorkItem
        {
            MessageId = Guid.NewGuid(),
            DeliveryAttemptId = 1,
            AttemptNumber = 1,
            CorrelationId = Guid.NewGuid().ToString("N"),
            EnvelopeFrom = "sender@example.test",
            Recipients = ["recipient@example.test"],
            SpoolFileRelativePath = "message.eml",
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
    }

    private static async Task ProcessWorkItemAsync(
        QueueDeliveryWorker worker,
        DeliveryWorkItem workItem,
        RelayConfigurationSnapshot configuration)
    {
        var method = typeof(QueueDeliveryWorker).GetMethod(
            "ProcessWorkItemAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task)method!.Invoke(worker, [workItem, configuration, CancellationToken.None])!;
        await task;
    }

    private sealed class TestQueueService : IMessageQueueService
    {
        public int MarkDeliveredCallCount { get; private set; }

        public int MarkFailedCallCount { get; private set; }

        public Exception? MarkDeliveredException { get; init; }

        public DeliveryResult? LastFailureResult { get; private set; }

        public Task EnqueueAsync(NewQueuedMessageRequest request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<DeliveryWorkItem?> TryClaimNextAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task MarkDeliveredAsync(DeliveryWorkItem workItem, DeliveryResult result, CancellationToken cancellationToken)
        {
            MarkDeliveredCallCount += 1;
            if (MarkDeliveredException is not null)
            {
                throw MarkDeliveredException;
            }

            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            DeliveryWorkItem workItem,
            DeliveryResult result,
            RelayConfigurationSnapshot configuration,
            CancellationToken cancellationToken)
        {
            MarkFailedCallCount += 1;
            LastFailureResult = result;
            return Task.CompletedTask;
        }

        public Task<QueueActionResult> RetryNowAsync(Guid messageId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<QueueActionResult> PurgeAsync(Guid messageId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<int> CleanupAsync(RelayConfigurationSnapshot configuration, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestUpstreamDeliveryService : IUpstreamDeliveryService
    {
        public DeliveryResult? Result { get; init; }

        public Exception? Exception { get; init; }

        public Task<DeliveryResult> DeliverAsync(
            DeliveryWorkItem workItem,
            RelayConfigurationSnapshot configuration,
            CancellationToken cancellationToken)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Result ?? new DeliveryResult { Succeeded = true });
        }
    }

    private sealed class TestOperationalEventService : IOperationalEventService
    {
        public List<OperationalEventRequest> Events { get; } = new();

        public Task WriteAsync(OperationalEventRequest request, CancellationToken cancellationToken = default)
        {
            Events.Add(request);
            return Task.CompletedTask;
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

    private sealed class TestQueueSignal : IQueueSignal
    {
        public void Pulse()
        {
        }

        public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
