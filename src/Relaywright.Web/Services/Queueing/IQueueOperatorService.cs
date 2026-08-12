namespace Relaywright.Web.Services.Queueing;

public interface IQueueOperatorService
{
    Task<QueueActionResult> RetryNowAsync(Guid messageId, CancellationToken cancellationToken);

    Task<QueueBulkActionResult> RetryNowAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken);

    Task<QueueActionResult> PurgeAsync(Guid messageId, CancellationToken cancellationToken);

    Task<QueueBulkActionResult> PurgeAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken);
}
