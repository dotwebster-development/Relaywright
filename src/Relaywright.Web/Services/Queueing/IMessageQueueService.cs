using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Queueing;

public interface IMessageQueueService
{
    Task EnqueueAsync(NewQueuedMessageRequest request, CancellationToken cancellationToken);

    Task<DeliveryWorkItem?> TryClaimNextAsync(CancellationToken cancellationToken);

    Task MarkDeliveredAsync(DeliveryWorkItem workItem, DeliveryResult result, CancellationToken cancellationToken);

    Task MarkFailedAsync(
        DeliveryWorkItem workItem,
        DeliveryResult result,
        RelayConfigurationSnapshot configuration,
        CancellationToken cancellationToken);

    Task<QueueActionResult> RetryNowAsync(Guid messageId, CancellationToken cancellationToken);

    Task<QueueBulkActionResult> RetryNowAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken);

    Task<QueueActionResult> PurgeAsync(Guid messageId, CancellationToken cancellationToken);

    Task<QueueBulkActionResult> PurgeAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken);

    Task<int> CleanupAsync(RelayConfigurationSnapshot configuration, CancellationToken cancellationToken);
}
