using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Backups;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.Queueing;

public sealed class QueueOperatorService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IMessageSpoolService spoolService,
    IBackupCoordinator backupCoordinator,
    IOperationalEventService eventService,
    IQueueSignal queueSignal,
    ILogger<QueueOperatorService> logger,
    TimeProvider? timeProvider = null) : IQueueOperatorService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<QueueActionResult> RetryNowAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var message = await dbContext.QueuedMessages.SingleOrDefaultAsync(x => x.Id == messageId, cancellationToken);
        if (message is null)
        {
            logger.LogWarning("Manual retry rejected because message was not found. MessageId={MessageId}", messageId);
            return QueueActionResult.Failure(QueueActionOutcome.NotFound, "Message not found.");
        }

        if (!QueueStateTransitionPolicy.CanTransition(message.Status, QueuedMessageStatus.RetryScheduled))
        {
            return QueueActionResult.Failure(QueueActionOutcome.InvalidState, message.Status switch
            {
                QueuedMessageStatus.Delivered => "Delivered messages cannot be retried.",
                QueuedMessageStatus.InProgress => "Message is currently being delivered and cannot be retried yet.",
                QueuedMessageStatus.Expired => "Expired messages cannot be retried.",
                _ => $"Messages in the {message.Status} state cannot be retried."
            });
        }

        var previousStatus = message.Status;
        message.Status = QueuedMessageStatus.RetryScheduled;
        message.NextAttemptAtUtc = clock.GetUtcNow();
        message.LastError = null;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning(
                "Manual retry rejected because the message state changed concurrently. MessageId={MessageId}; PreviousStatus={PreviousStatus}",
                messageId,
                previousStatus);
            return QueueActionResult.Failure(
                QueueActionOutcome.InvalidState,
                "Message state changed while the retry was being scheduled. Refresh and try again.");
        }
        queueSignal.Pulse();

        logger.LogInformation(
            "Manual retry scheduled. MessageId={MessageId}; PreviousStatus={PreviousStatus}; NextAttemptUtc={NextAttemptUtc}",
            messageId,
            previousStatus,
            message.NextAttemptAtUtc);
        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.Queue,
            QueuedMessageId = messageId,
            Message = "Manual retry requested."
        }, cancellationToken);
        return QueueActionResult.Success("Retry scheduled.");
    }

    public async Task<QueueBulkActionResult> RetryNowAsync(
        IReadOnlyCollection<Guid> messageIds,
        CancellationToken cancellationToken)
    {
        var requested = messageIds.Distinct().ToArray();
        var results = await ExecuteBulkAsync(requested, RetryNowAsync, cancellationToken);
        return new QueueBulkActionResult
        {
            Requested = requested.Length,
            Succeeded = results.Succeeded,
            Rejected = results.Rejected,
            Missing = results.Missing
        };
    }

    public async Task<QueueActionResult> PurgeAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var message = await dbContext.QueuedMessages.SingleOrDefaultAsync(x => x.Id == messageId, cancellationToken);
        if (message is null)
        {
            return QueueActionResult.Failure(QueueActionOutcome.NotFound, "Message not found.");
        }

        if (message.Status == QueuedMessageStatus.InProgress)
        {
            return QueueActionResult.Failure(
                QueueActionOutcome.InvalidState,
                "Message is currently being delivered and cannot be purged.");
        }

        var previousStatus = message.Status;
        var spoolPath = message.SpoolFileRelativePath;
        dbContext.QueuedMessages.Remove(message);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning(
                "Purge rejected because the message state changed concurrently. MessageId={MessageId}; PreviousStatus={PreviousStatus}",
                messageId,
                previousStatus);
            return QueueActionResult.Failure(
                QueueActionOutcome.InvalidState,
                "Message state changed while it was being purged. Refresh and try again.");
        }

        try
        {
            await using var backupLock = await backupCoordinator.AcquireSpoolDeletionLockAsync(cancellationToken);
            await spoolService.DeleteIfExistsAsync(spoolPath, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Queued message metadata was purged but spool deletion failed. MessageId={MessageId}; PreviousStatus={PreviousStatus}; SpoolPath={SpoolPath}",
                messageId,
                previousStatus,
                spoolPath);
            await eventService.WriteAsync(new OperationalEventRequest
            {
                Severity = EventSeverity.Error,
                Category = OperationalEventCategory.Queue,
                QueuedMessageId = messageId,
                Message = "Queued message metadata was purged, but its spool file could not be deleted.",
                Detail = exception.Message
            }, cancellationToken);
            return QueueActionResult.Failure(
                QueueActionOutcome.SpoolDeleteFailed,
                "Queued message metadata was purged, but the spool file could not be deleted.");
        }

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

    public async Task<QueueBulkActionResult> PurgeAsync(
        IReadOnlyCollection<Guid> messageIds,
        CancellationToken cancellationToken)
    {
        var requested = messageIds.Distinct().ToArray();
        var results = await ExecuteBulkAsync(requested, PurgeAsync, cancellationToken);
        return new QueueBulkActionResult
        {
            Requested = requested.Length,
            Succeeded = results.Succeeded,
            Rejected = results.Rejected,
            Missing = results.Missing,
            SpoolDeleteFailures = results.SpoolFailures
        };
    }

    private static async Task<BulkCounts> ExecuteBulkAsync(
        IReadOnlyCollection<Guid> messageIds,
        Func<Guid, CancellationToken, Task<QueueActionResult>> action,
        CancellationToken cancellationToken)
    {
        var counts = new BulkCounts();
        foreach (var messageId in messageIds)
        {
            var result = await action(messageId, cancellationToken);
            if (result.Succeeded)
            {
                counts.Succeeded++;
            }
            else if (result.Outcome == QueueActionOutcome.NotFound)
            {
                counts.Missing++;
            }
            else if (result.Outcome == QueueActionOutcome.SpoolDeleteFailed)
            {
                counts.SpoolFailures++;
            }
            else
            {
                counts.Rejected++;
            }
        }

        return counts;
    }

    private sealed class BulkCounts
    {
        public int Succeeded { get; set; }

        public int Rejected { get; set; }

        public int Missing { get; set; }

        public int SpoolFailures { get; set; }
    }
}
