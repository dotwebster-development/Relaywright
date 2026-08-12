using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.Queueing;

public sealed class MessageQueueService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IOperationalEventService eventService,
    IQueueSignal queueSignal,
    QueueClaimService claimService,
    QueueDeliveryStateService deliveryStateService,
    ILogger<MessageQueueService> logger) : IMessageQueueService
{
    public async Task EnqueueAsync(NewQueuedMessageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var queuedMessage = new QueuedMessage
        {
            Id = request.MessageId,
            SessionId = request.SessionId,
            RemoteIpAddress = request.RemoteIpAddress,
            EnvelopeFrom = request.EnvelopeFrom,
            MessageSizeBytes = request.MessageSizeBytes,
            SpoolFileRelativePath = request.SpoolFileRelativePath,
            Status = QueuedMessageStatus.Pending,
            AcceptedUtc = request.AcceptedUtc,
            CreatedUtc = request.AcceptedUtc,
            NextAttemptAtUtc = request.AcceptedUtc,
            ExpiresUtc = request.AcceptedUtc.AddHours(request.MessageExpirationHours)
        };

        foreach (var recipient in request.Recipients.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            queuedMessage.Recipients.Add(new QueuedMessageRecipient
            {
                RecipientAddress = recipient
            });
        }

        dbContext.QueuedMessages.Add(queuedMessage);
        await dbContext.SaveChangesAsync(cancellationToken);
        queueSignal.Pulse();

        logger.LogInformation(
            "Message enqueued. MessageId={MessageId}; SessionId={SessionId}; RemoteIp={RemoteIp}; RecipientCount={RecipientCount}; Bytes={Bytes}; ExpiresUtc={ExpiresUtc}; SpoolPath={SpoolPath}",
            queuedMessage.Id,
            queuedMessage.SessionId,
            queuedMessage.RemoteIpAddress,
            queuedMessage.Recipients.Count,
            queuedMessage.MessageSizeBytes,
            queuedMessage.ExpiresUtc,
            queuedMessage.SpoolFileRelativePath);

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.Queue,
            SessionId = request.SessionId,
            QueuedMessageId = request.MessageId,
            RemoteIpAddress = request.RemoteIpAddress,
            Message = $"Message accepted into queue for {queuedMessage.Recipients.Count} recipient(s)."
        }, cancellationToken);
    }

    public Task<DeliveryWorkItem?> TryClaimNextAsync(CancellationToken cancellationToken)
    {
        return claimService.TryClaimNextAsync(cancellationToken);
    }

    public Task MarkDeliveredAsync(
        DeliveryWorkItem workItem,
        DeliveryResult result,
        CancellationToken cancellationToken)
    {
        return deliveryStateService.MarkDeliveredAsync(workItem, result, cancellationToken);
    }

    public Task MarkFailedAsync(
        DeliveryWorkItem workItem,
        DeliveryResult result,
        RelayConfigurationSnapshot configuration,
        CancellationToken cancellationToken)
    {
        return deliveryStateService.MarkFailedAsync(workItem, result, configuration, cancellationToken);
    }
}
