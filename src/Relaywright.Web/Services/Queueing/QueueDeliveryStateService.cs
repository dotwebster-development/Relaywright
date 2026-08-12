using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.Queueing;

public sealed class QueueDeliveryStateService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    RetryDelayCalculator retryDelayCalculator,
    IOperationalEventService eventService,
    ILogger<QueueDeliveryStateService> logger,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task MarkDeliveredAsync(
        DeliveryWorkItem workItem,
        DeliveryResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(result);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var queuedMessage = await dbContext.QueuedMessages.SingleAsync(x => x.Id == workItem.MessageId, cancellationToken);
        var attempt = await dbContext.DeliveryAttempts.SingleAsync(x => x.Id == workItem.DeliveryAttemptId, cancellationToken);
        var now = clock.GetUtcNow();

        if (attempt.CompletedUtc is not null)
        {
            if (attempt.Succeeded && queuedMessage.Status == QueuedMessageStatus.Delivered)
            {
                logger.LogDebug(
                    "Delivery completion was already persisted. MessageId={MessageId}; AttemptNumber={AttemptNumber}",
                    workItem.MessageId,
                    workItem.AttemptNumber);
                return;
            }

            throw new InvalidOperationException(
                $"Delivery attempt {workItem.DeliveryAttemptId} is already completed and cannot be marked delivered.");
        }

        QueueStateTransitionPolicy.EnsureAllowed(queuedMessage.Status, QueuedMessageStatus.Delivered);
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
        ArgumentNullException.ThrowIfNull(configuration);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var queuedMessage = await dbContext.QueuedMessages.SingleAsync(x => x.Id == workItem.MessageId, cancellationToken);
        var attempt = await dbContext.DeliveryAttempts.SingleAsync(x => x.Id == workItem.DeliveryAttemptId, cancellationToken);
        var now = clock.GetUtcNow();

        if (attempt.CompletedUtc is not null)
        {
            if (!attempt.Succeeded)
            {
                logger.LogDebug(
                    "Delivery failure was already persisted. MessageId={MessageId}; AttemptNumber={AttemptNumber}; Status={Status}",
                    workItem.MessageId,
                    workItem.AttemptNumber,
                    queuedMessage.Status);
                return;
            }

            throw new InvalidOperationException(
                $"Delivery attempt {workItem.DeliveryAttemptId} is already completed successfully and cannot be marked failed.");
        }

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
        var nextStatus = shouldFailPermanently
            ? queuedMessage.ExpiresUtc <= now
                ? QueuedMessageStatus.Expired
                : QueuedMessageStatus.Failed
            : QueuedMessageStatus.RetryScheduled;

        QueueStateTransitionPolicy.EnsureAllowed(queuedMessage.Status, nextStatus);
        queuedMessage.Status = nextStatus;

        if (!shouldFailPermanently)
        {
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
}
