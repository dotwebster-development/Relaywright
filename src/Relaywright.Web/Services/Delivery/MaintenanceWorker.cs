using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Delivery;

public sealed class MaintenanceWorker(
    IRelayConfigurationService relayConfigurationService,
    IMessageQueueService queueService,
    IOperationalEventService eventService,
    ILogger<MaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Maintenance worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                logger.LogDebug("Maintenance cleanup tick started.");
                var configuration = await relayConfigurationService.GetSnapshotAsync(stoppingToken);
                var cleaned = await queueService.CleanupAsync(configuration, stoppingToken);

                if (cleaned > 0)
                {
                    logger.LogInformation("Maintenance cleanup removed {Count} record(s).", cleaned);
                }
                else
                {
                    logger.LogDebug("Maintenance cleanup completed with no removed records.");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Maintenance cleanup failed.");

                await eventService.WriteAsync(new OperationalEventRequest
                {
                    Severity = EventSeverity.Error,
                    Category = OperationalEventCategory.System,
                    Message = "Maintenance cleanup failed.",
                    Detail = exception.ToString()
                }, stoppingToken);
            }

            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }

        logger.LogInformation("Maintenance worker stopped.");
    }
}
