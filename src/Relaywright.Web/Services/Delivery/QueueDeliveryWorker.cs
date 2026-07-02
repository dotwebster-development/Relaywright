using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Runtime;

namespace Relaywright.Web.Services.Delivery;

public sealed class QueueDeliveryWorker(
    IRelayConfigurationService relayConfigurationService,
    IMessageQueueService queueService,
    IUpstreamDeliveryService upstreamDeliveryService,
    IQueueSignal queueSignal,
    IOperationalEventService eventService,
    IRuntimeStatusService runtimeStatusService,
    ILogger<QueueDeliveryWorker> logger) : BackgroundService
{
    private const int StateWriteRetryCount = 3;
    private static readonly TimeSpan StateWriteRetryDelay = TimeSpan.FromMilliseconds(100);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var activeDeliveries = new List<Task>();

        logger.LogInformation("Queue delivery worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var configuration = await relayConfigurationService.GetSnapshotAsync(stoppingToken);
                activeDeliveries.RemoveAll(task => task.IsCompleted);
                var paused = await runtimeStatusService.IsDeliveryPausedAsync(stoppingToken);
                runtimeStatusService.ReportDeliveryWorkerState(
                    paused ? "Paused" : "Running",
                    activeDeliveries.Count,
                    paused ? "Outbound delivery is paused." : "Outbound delivery is enabled.");

                logger.LogDebug(
                    "Queue delivery worker loop tick. ActiveDeliveries={ActiveDeliveries}; DeliveryConcurrency={DeliveryConcurrency}",
                    activeDeliveries.Count,
                    configuration.DeliveryConcurrency);

                if (paused)
                {
                    if (activeDeliveries.Count == 0)
                    {
                        await queueSignal.WaitAsync(TimeSpan.FromSeconds(15), stoppingToken);
                    }
                    else
                    {
                        await Task.WhenAny(activeDeliveries).WaitAsync(stoppingToken);
                    }

                    continue;
                }

                while (activeDeliveries.Count < configuration.DeliveryConcurrency && !stoppingToken.IsCancellationRequested)
                {
                    var workItem = await queueService.TryClaimNextAsync(stoppingToken);
                    if (workItem is null)
                    {
                        logger.LogDebug("No queue work item claimed on this tick.");
                        break;
                    }

                    logger.LogInformation(
                        "Queue worker claimed message. MessageId={MessageId}; AttemptNumber={AttemptNumber}; CorrelationId={CorrelationId}; RecipientCount={RecipientCount}; ExpiresUtc={ExpiresUtc}",
                        workItem.MessageId,
                        workItem.AttemptNumber,
                        workItem.CorrelationId,
                        workItem.Recipients.Count,
                        workItem.ExpiresUtc);

                    activeDeliveries.Add(ProcessWorkItemAsync(workItem, configuration, stoppingToken));
                }

                if (activeDeliveries.Count == 0)
                {
                    logger.LogDebug("Queue delivery worker waiting for signal or timeout.");
                    await queueSignal.WaitAsync(TimeSpan.FromSeconds(15), stoppingToken);
                }
                else
                {
                    logger.LogDebug("Queue delivery worker waiting for one active delivery to complete. ActiveDeliveries={ActiveDeliveries}", activeDeliveries.Count);
                    await Task.WhenAny(activeDeliveries).WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                runtimeStatusService.ReportDeliveryWorkerState("Faulted", activeDeliveries.Count, "Queue delivery worker loop failed.", exception);
                logger.LogError(exception, "Queue delivery worker loop failed unexpectedly.");

                await eventService.WriteAsync(new OperationalEventRequest
                {
                    Severity = EventSeverity.Error,
                    Category = OperationalEventCategory.System,
                    Message = "Queue delivery worker loop failed unexpectedly.",
                    Detail = exception.ToString()
                }, stoppingToken);

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        try
        {
            await Task.WhenAll(activeDeliveries);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.System,
            Message = "Queue delivery worker stopped."
        });

        runtimeStatusService.ReportDeliveryWorkerState("Stopped", 0, "Queue delivery worker stopped.");
        logger.LogInformation("Queue delivery worker stopped.");
    }

    private async Task ProcessWorkItemAsync(
        DeliveryWorkItem workItem,
        RelayConfigurationSnapshot configuration,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Delivering queued message {MessageId} attempt {AttemptNumber}", workItem.MessageId, workItem.AttemptNumber);

            var result = await upstreamDeliveryService.DeliverAsync(workItem, configuration, cancellationToken);
            if (result.Succeeded)
            {
                var markedDelivered = await TryMarkDeliveredAsync(workItem, result, cancellationToken);
                if (!markedDelivered)
                {
                    return;
                }

                logger.LogInformation(
                    "Delivery processing completed successfully. MessageId={MessageId}; AttemptNumber={AttemptNumber}",
                    workItem.MessageId,
                    workItem.AttemptNumber);
            }
            else
            {
                var markedFailed = await TryMarkFailedAsync(workItem, result, configuration, cancellationToken);
                if (!markedFailed)
                {
                    return;
                }

                logger.LogWarning(
                    "Delivery processing completed with failure. MessageId={MessageId}; AttemptNumber={AttemptNumber}; FailureCategory={FailureCategory}; Permanent={Permanent}; ExceptionType={ExceptionType}",
                    workItem.MessageId,
                    workItem.AttemptNumber,
                    result.FailureCategory,
                    result.IsPermanentFailure,
                    result.ExceptionType);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Delivery processing failed unexpectedly for {MessageId}", workItem.MessageId);

            var result = new DeliveryResult
            {
                FailureCategory = DeliveryFailureCategory.Transient,
                ExceptionType = exception.GetType().Name,
                ErrorDetail = exception.Message
            };

            await TryMarkFailedAsync(workItem, result, configuration, cancellationToken);
        }
    }

    private async Task<bool> TryMarkDeliveredAsync(
        DeliveryWorkItem workItem,
        DeliveryResult result,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= StateWriteRetryCount; attempt++)
        {
            try
            {
                await queueService.MarkDeliveredAsync(workItem, result, cancellationToken);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to mark delivered message after upstream success. MessageId={MessageId}; AttemptNumber={AttemptNumber}; StateWriteAttempt={StateWriteAttempt}",
                    workItem.MessageId,
                    workItem.AttemptNumber,
                    attempt);

                if (attempt == StateWriteRetryCount)
                {
                    await eventService.WriteAsync(new OperationalEventRequest
                    {
                        Severity = EventSeverity.Error,
                        Category = OperationalEventCategory.Delivery,
                        QueuedMessageId = workItem.MessageId,
                        RemoteIpAddress = workItem.RemoteIpAddress,
                        Message = "Queued message was accepted upstream, but local delivered state could not be persisted.",
                        Detail = exception.ToString()
                    }, cancellationToken);

                    return false;
                }

                await Task.Delay(StateWriteRetryDelay, cancellationToken);
            }
        }

        return false;
    }

    private async Task<bool> TryMarkFailedAsync(
        DeliveryWorkItem workItem,
        DeliveryResult result,
        RelayConfigurationSnapshot configuration,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= StateWriteRetryCount; attempt++)
        {
            try
            {
                await queueService.MarkFailedAsync(workItem, result, configuration, cancellationToken);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to persist delivery failure state. MessageId={MessageId}; AttemptNumber={AttemptNumber}; StateWriteAttempt={StateWriteAttempt}",
                    workItem.MessageId,
                    workItem.AttemptNumber,
                    attempt);

                if (attempt == StateWriteRetryCount)
                {
                    await eventService.WriteAsync(new OperationalEventRequest
                    {
                        Severity = EventSeverity.Error,
                        Category = OperationalEventCategory.Delivery,
                        QueuedMessageId = workItem.MessageId,
                        RemoteIpAddress = workItem.RemoteIpAddress,
                        Message = "Delivery failed, but local failure state could not be persisted.",
                        Detail = exception.ToString()
                    }, cancellationToken);

                    return false;
                }

                await Task.Delay(StateWriteRetryDelay, cancellationToken);
            }
        }

        return false;
    }
}
