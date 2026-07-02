using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Runtime;

namespace Relaywright.Web.Services.Delivery;

public sealed class MaintenanceWorker(
    IRelayConfigurationService relayConfigurationService,
    IMessageQueueService queueService,
    IOperationalEventService eventService,
    IRuntimeStatusService runtimeStatusService,
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
                runtimeStatusService.ReportMaintenanceWorkerState("Running", detail: "Maintenance cleanup tick started.");
                var configuration = await relayConfigurationService.GetSnapshotAsync(stoppingToken);
                var cleaned = await queueService.CleanupAsync(configuration, stoppingToken);
                runtimeStatusService.ReportMaintenanceWorkerState("Running", cleaned, "Maintenance cleanup completed.");

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
                runtimeStatusService.ReportMaintenanceWorkerState("Faulted", detail: "Maintenance cleanup failed.", exception: exception);
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

        runtimeStatusService.ReportMaintenanceWorkerState("Stopped", detail: "Maintenance worker stopped.");
        logger.LogInformation("Maintenance worker stopped.");
    }
}
