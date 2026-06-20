using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Delivery;

public sealed class QueueDeliveryWorker(
    IRelayConfigurationService relayConfigurationService,
    IMessageQueueService queueService,
    IUpstreamDeliveryService upstreamDeliveryService,
    IQueueSignal queueSignal,
    IOperationalEventService eventService,
    ILogger<QueueDeliveryWorker> logger) : BackgroundService
{
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

                logger.LogDebug(
                    "Queue delivery worker loop tick. ActiveDeliveries={ActiveDeliveries}; DeliveryConcurrency={DeliveryConcurrency}",
                    activeDeliveries.Count,
                    configuration.DeliveryConcurrency);

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
                await queueService.MarkDeliveredAsync(workItem, result, cancellationToken);
                logger.LogInformation(
                    "Delivery processing completed successfully. MessageId={MessageId}; AttemptNumber={AttemptNumber}",
                    workItem.MessageId,
                    workItem.AttemptNumber);
            }
            else
            {
                await queueService.MarkFailedAsync(workItem, result, configuration, cancellationToken);
                logger.LogWarning(
                    "Delivery processing completed with failure. MessageId={MessageId}; AttemptNumber={AttemptNumber}; FailureCategory={FailureCategory}; Permanent={Permanent}; ExceptionType={ExceptionType}",
                    workItem.MessageId,
                    workItem.AttemptNumber,
                    result.FailureCategory,
                    result.IsPermanentFailure,
                    result.ExceptionType);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Delivery processing failed unexpectedly for {MessageId}", workItem.MessageId);

            await eventService.WriteAsync(new OperationalEventRequest
            {
                Severity = EventSeverity.Error,
                Category = OperationalEventCategory.Delivery,
                QueuedMessageId = workItem.MessageId,
                RemoteIpAddress = workItem.RemoteIpAddress,
                Message = "Delivery processing failed unexpectedly.",
                Detail = exception.ToString()
            });
        }
    }
}
