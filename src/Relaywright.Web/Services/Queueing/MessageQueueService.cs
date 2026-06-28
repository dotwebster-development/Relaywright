using System.Data;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.Queueing;

public sealed class MessageQueueService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    RetryDelayCalculator retryDelayCalculator,
    IMessageSpoolService spoolService,
    IOperationalEventService eventService,
    IQueueSignal queueSignal,
    ILogger<MessageQueueService> logger) : IMessageQueueService
{
    private const int ClaimRetryLimit = 3;

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

    public async Task<DeliveryWorkItem?> TryClaimNextAsync(CancellationToken cancellationToken)
    {
        for (var claimAttempt = 1; claimAttempt <= ClaimRetryLimit; claimAttempt++)
        {
            var now = DateTimeOffset.UtcNow;
            var staleThreshold = now.AddMinutes(-15);

            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            var candidate = await dbContext.QueuedMessages
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "QueuedMessages"
                    WHERE (("Status" = {(int)QueuedMessageStatus.Pending} OR "Status" = {(int)QueuedMessageStatus.RetryScheduled})
                        AND "NextAttemptAtUtc" <= {now})
                        OR ("Status" = {(int)QueuedMessageStatus.InProgress}
                            AND "LastAttemptStartedUtc" IS NOT NULL
                            AND "LastAttemptStartedUtc" <= {staleThreshold})
                    ORDER BY CASE
                        WHEN "Status" = {(int)QueuedMessageStatus.InProgress} THEN "LastAttemptStartedUtc"
                        ELSE "NextAttemptAtUtc"
                    END ASC, "CreatedUtc" ASC
                    LIMIT 1
                    """)
                .AsNoTracking()
                .Select(x => new
                {
                    x.Id,
                    x.Status,
                    x.AttemptCount
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (candidate is null)
            {
                logger.LogDebug("No eligible queue work found.");
                return null;
            }

            var attemptNumber = candidate.AttemptCount + 1;
            var updated = await dbContext.QueuedMessages
                .Where(x =>
                    x.Id == candidate.Id
                    && x.Status == candidate.Status
                    && x.AttemptCount == candidate.AttemptCount)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, QueuedMessageStatus.InProgress)
                    .SetProperty(x => x.AttemptCount, attemptNumber)
                    .SetProperty(x => x.LastAttemptStartedUtc, (DateTimeOffset?)now),
                    cancellationToken);

            if (updated == 0)
            {
                logger.LogDebug(
                    "Queue claim candidate was already claimed by another worker. MessageId={MessageId}; ClaimAttempt={ClaimAttempt}",
                    candidate.Id,
                    claimAttempt);
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            var deliveryAttempt = new DeliveryAttempt
            {
                QueuedMessageId = candidate.Id,
                AttemptNumber = attemptNumber,
                StartedUtc = now
            };

            dbContext.DeliveryAttempts.Add(deliveryAttempt);
            await dbContext.SaveChangesAsync(cancellationToken);

            var queuedMessage = await dbContext.QueuedMessages
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Recipients)
                .SingleAsync(x => x.Id == candidate.Id, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Claimed queued message for delivery. MessageId={MessageId}; PreviousStatus={PreviousStatus}; AttemptNumber={AttemptNumber}; RecipientCount={RecipientCount}; RemoteIp={RemoteIp}; ExpiresUtc={ExpiresUtc}; SpoolPath={SpoolPath}",
                queuedMessage.Id,
                candidate.Status,
                attemptNumber,
                queuedMessage.Recipients.Count,
                queuedMessage.RemoteIpAddress,
                queuedMessage.ExpiresUtc,
                queuedMessage.SpoolFileRelativePath);

            return new DeliveryWorkItem
            {
                MessageId = queuedMessage.Id,
                DeliveryAttemptId = deliveryAttempt.Id,
                AttemptNumber = attemptNumber,
                CorrelationId = queuedMessage.CorrelationId,
                EnvelopeFrom = queuedMessage.EnvelopeFrom,
                Recipients = queuedMessage.Recipients
                    .Select(x => x.RecipientAddress)
                    .ToArray(),
                SpoolFileRelativePath = queuedMessage.SpoolFileRelativePath,
                RemoteIpAddress = queuedMessage.RemoteIpAddress,
                ExpiresUtc = queuedMessage.ExpiresUtc
            };
        }

        logger.LogDebug("No queue work was claimed after retrying contended candidates.");
        return null;
    }

    public async Task MarkDeliveredAsync(DeliveryWorkItem workItem, DeliveryResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var queuedMessage = await dbContext.QueuedMessages.SingleAsync(x => x.Id == workItem.MessageId, cancellationToken);
        var attempt = await dbContext.DeliveryAttempts.SingleAsync(x => x.Id == workItem.DeliveryAttemptId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        queuedMessage.Status = QueuedMessageStatus.Delivered;
        queuedMessage.LastAttemptCompletedUtc = now;
        queuedMessage.DeliveredUtc = now;
        queuedMessage.FailureCategory = DeliveryFailureCategory.None;
        queuedMessage.LastResponseCode = result.ResponseCode;
        queuedMessage.LastResponseText = result.ResponseText;
        queuedMessage.LastError = null;

        attempt.Succeeded = true;
        attempt.CompletedUtc = now;
        attempt.ResponseCode = result.ResponseCode;
        attempt.ResponseText = result.ResponseText;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Marked queued message delivered. MessageId={MessageId}; AttemptNumber={AttemptNumber}; ResponseCode={ResponseCode}; ResponseText={ResponseText}",
            workItem.MessageId,
            workItem.AttemptNumber,
            result.ResponseCode,
            result.ResponseText);

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.Delivery,
            QueuedMessageId = workItem.MessageId,
            RemoteIpAddress = workItem.RemoteIpAddress,
            Message = "Queued message delivered successfully.",
            Detail = result.ResponseText
        }, cancellationToken);
    }

    public async Task MarkFailedAsync(
        DeliveryWorkItem workItem,
        DeliveryResult result,
        RelayConfigurationSnapshot configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(result);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var queuedMessage = await dbContext.QueuedMessages.SingleAsync(x => x.Id == workItem.MessageId, cancellationToken);
        var attempt = await dbContext.DeliveryAttempts.SingleAsync(x => x.Id == workItem.DeliveryAttemptId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        attempt.Succeeded = false;
        attempt.CompletedUtc = now;
        attempt.ResponseCode = result.ResponseCode;
        attempt.ResponseText = result.ResponseText;
        attempt.ExceptionType = result.ExceptionType;
        attempt.ExceptionMessage = result.ErrorDetail;
        attempt.FailureCategory = result.FailureCategory;

        queuedMessage.LastAttemptCompletedUtc = now;
        queuedMessage.LastResponseCode = result.ResponseCode;
        queuedMessage.LastResponseText = result.ResponseText;
        queuedMessage.LastError = result.ErrorDetail;
        queuedMessage.FailureCategory = result.FailureCategory;

        var shouldFailPermanently =
            result.IsPermanentFailure
            || queuedMessage.AttemptCount >= configuration.MaxRetryCount
            || queuedMessage.ExpiresUtc <= now;

        if (shouldFailPermanently)
        {
            queuedMessage.Status = queuedMessage.ExpiresUtc <= now
                ? QueuedMessageStatus.Expired
                : QueuedMessageStatus.Failed;
        }
        else
        {
            queuedMessage.Status = QueuedMessageStatus.RetryScheduled;
            queuedMessage.NextAttemptAtUtc = now.Add(retryDelayCalculator.Calculate(
                queuedMessage.AttemptCount,
                configuration.InitialRetryDelaySeconds,
                configuration.MaxRetryDelaySeconds));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Log(
            shouldFailPermanently ? LogLevel.Error : LogLevel.Warning,
            "Marked queued message delivery attempt failed. MessageId={MessageId}; AttemptNumber={AttemptNumber}; Status={Status}; FailureCategory={FailureCategory}; Permanent={Permanent}; NextAttemptUtc={NextAttemptUtc}; ResponseCode={ResponseCode}; ExceptionType={ExceptionType}; ErrorDetail={ErrorDetail}",
            workItem.MessageId,
            workItem.AttemptNumber,
            queuedMessage.Status,
            result.FailureCategory,
            shouldFailPermanently,
            queuedMessage.Status == QueuedMessageStatus.RetryScheduled ? queuedMessage.NextAttemptAtUtc : null,
            result.ResponseCode,
            result.ExceptionType,
            result.ErrorDetail ?? result.ResponseText);

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Severity = shouldFailPermanently ? EventSeverity.Error : EventSeverity.Warning,
            Category = OperationalEventCategory.Delivery,
            QueuedMessageId = workItem.MessageId,
            RemoteIpAddress = workItem.RemoteIpAddress,
            Message = shouldFailPermanently
                ? "Queued message marked as failed."
                : $"Queued message scheduled for retry at {queuedMessage.NextAttemptAtUtc:O}.",
            Detail = result.ErrorDetail ?? result.ResponseText
        }, cancellationToken);
    }

    public async Task<QueueActionResult> RetryNowAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var queuedMessage = await dbContext.QueuedMessages.SingleOrDefaultAsync(x => x.Id == messageId, cancellationToken);

        if (queuedMessage is null)
        {
            logger.LogWarning("Manual retry rejected because message was not found. MessageId={MessageId}", messageId);
            return QueueActionResult.Failure("Message not found.");
        }

        if (queuedMessage.Status == QueuedMessageStatus.Delivered)
        {
            logger.LogWarning("Manual retry rejected for delivered message. MessageId={MessageId}", messageId);
            return QueueActionResult.Failure("Delivered messages cannot be retried.");
        }

        if (queuedMessage.Status == QueuedMessageStatus.InProgress)
        {
            logger.LogWarning("Manual retry rejected for in-progress message. MessageId={MessageId}", messageId);
            return QueueActionResult.Failure("Message is currently being delivered and cannot be retried yet.");
        }

        if (queuedMessage.Status == QueuedMessageStatus.Expired)
        {
            logger.LogWarning("Manual retry rejected for expired message. MessageId={MessageId}", messageId);
            return QueueActionResult.Failure("Expired messages cannot be retried.");
        }

        var previousStatus = queuedMessage.Status;
        queuedMessage.Status = QueuedMessageStatus.RetryScheduled;
        queuedMessage.NextAttemptAtUtc = DateTimeOffset.UtcNow;
        queuedMessage.LastError = null;

        await dbContext.SaveChangesAsync(cancellationToken);
        queueSignal.Pulse();

        logger.LogInformation(
            "Manual retry scheduled. MessageId={MessageId}; PreviousStatus={PreviousStatus}; NextAttemptUtc={NextAttemptUtc}",
            messageId,
            previousStatus,
            queuedMessage.NextAttemptAtUtc);

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.Queue,
            QueuedMessageId = messageId,
            Message = "Manual retry requested."
        }, cancellationToken);

        return QueueActionResult.Success("Retry scheduled.");
    }

    public async Task<QueueActionResult> PurgeAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var queuedMessage = await dbContext.QueuedMessages.SingleOrDefaultAsync(x => x.Id == messageId, cancellationToken);
        if (queuedMessage is null)
        {
            logger.LogWarning("Purge rejected because message was not found. MessageId={MessageId}", messageId);
            return QueueActionResult.Failure("Message not found.");
        }

        if (queuedMessage.Status == QueuedMessageStatus.InProgress)
        {
            logger.LogWarning("Purge rejected for in-progress message. MessageId={MessageId}", messageId);
            return QueueActionResult.Failure("Message is currently being delivered and cannot be purged.");
        }

        var previousStatus = queuedMessage.Status;
        var spoolPath = queuedMessage.SpoolFileRelativePath;
        dbContext.QueuedMessages.Remove(queuedMessage);
        await dbContext.SaveChangesAsync(cancellationToken);
        await spoolService.DeleteIfExistsAsync(spoolPath, cancellationToken);

        logger.LogInformation(
            "Purged queued message. MessageId={MessageId}; PreviousStatus={PreviousStatus}; SpoolPath={SpoolPath}",
            messageId,
            previousStatus,
            spoolPath);

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.Queue,
            QueuedMessageId = messageId,
            Message = "Queued message purged."
        }, cancellationToken);

        return QueueActionResult.Success("Queued message purged.");
    }

    public async Task<int> CleanupAsync(RelayConfigurationSnapshot configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var deletedCount = 0;
        var deliveredCutoff = DateTimeOffset.UtcNow.AddHours(-configuration.DeliveredRetentionHours);
        var failedCutoff = DateTimeOffset.UtcNow.AddHours(-configuration.FailedRetentionHours);
        var eventCutoff = DateTimeOffset.UtcNow.AddHours(-configuration.EventRetentionHours);
        var now = DateTimeOffset.UtcNow;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var deliveredMessagesToDelete = await dbContext.QueuedMessages
            .FromSqlInterpolated($"""
                SELECT *
                FROM "QueuedMessages"
                WHERE "Status" = {(int)QueuedMessageStatus.Delivered}
                    AND "DeliveredUtc" IS NOT NULL
                    AND "DeliveredUtc" <= {deliveredCutoff}
                """)
            .ToListAsync(cancellationToken);

        var terminalMessagesToDelete = await dbContext.QueuedMessages
            .FromSqlInterpolated($"""
                SELECT *
                FROM "QueuedMessages"
                WHERE ("Status" = {(int)QueuedMessageStatus.Failed}
                        OR "Status" = {(int)QueuedMessageStatus.Expired})
                    AND "LastAttemptCompletedUtc" IS NOT NULL
                    AND "LastAttemptCompletedUtc" <= {failedCutoff}
                """)
            .ToListAsync(cancellationToken);

        var messagesToDelete = deliveredMessagesToDelete
            .Concat(terminalMessagesToDelete)
            .DistinctBy(x => x.Id)
            .ToList();

        var spoolPathsToDelete = messagesToDelete
            .Select(x => x.SpoolFileRelativePath)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        dbContext.QueuedMessages.RemoveRange(messagesToDelete);

        var expirableMessages = await dbContext.QueuedMessages
            .FromSqlInterpolated($"""
                SELECT *
                FROM "QueuedMessages"
                WHERE ("Status" = {(int)QueuedMessageStatus.Pending}
                        OR "Status" = {(int)QueuedMessageStatus.InProgress}
                        OR "Status" = {(int)QueuedMessageStatus.RetryScheduled})
                    AND "ExpiresUtc" <= {now}
                """)
            .ToListAsync(cancellationToken);

        foreach (var message in expirableMessages)
        {
            message.Status = QueuedMessageStatus.Expired;
            message.LastAttemptCompletedUtc ??= now;
            message.LastError = "Message expired before successful delivery.";
        }

        var eventsToDelete = await dbContext.OperationalEvents
            .FromSqlInterpolated($"""
                SELECT *
                FROM "OperationalEvents"
                WHERE "OccurredUtc" <= {eventCutoff}
                """)
            .ToListAsync(cancellationToken);

        dbContext.OperationalEvents.RemoveRange(eventsToDelete);
        deletedCount += messagesToDelete.Count + eventsToDelete.Count;

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var spoolPath in spoolPathsToDelete)
        {
            await spoolService.DeleteIfExistsAsync(spoolPath, cancellationToken);
        }

        logger.LogInformation(
            "Queue cleanup completed. DeletedMessages={DeletedMessages}; DeletedEvents={DeletedEvents}; ExpiredActiveMessages={ExpiredActiveMessages}; DeletedSpoolFiles={DeletedSpoolFiles}; DeliveredCutoffUtc={DeliveredCutoffUtc}; FailedCutoffUtc={FailedCutoffUtc}; EventCutoffUtc={EventCutoffUtc}",
            messagesToDelete.Count,
            eventsToDelete.Count,
            expirableMessages.Count(x => x.Status == QueuedMessageStatus.Expired && x.LastError == "Message expired before successful delivery."),
            spoolPathsToDelete.Count,
            deliveredCutoff,
            failedCutoff,
            eventCutoff);

        return deletedCount;
    }

}
