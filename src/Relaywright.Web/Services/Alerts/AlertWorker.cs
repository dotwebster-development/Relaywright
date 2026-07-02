using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.Alerts;

public sealed class AlertWorker(
    IAlertService alertService,
    IOperationalEventService eventService,
    ILogger<AlertWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartupGracePeriod = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Alert worker started.");

        try
        {
            await Task.Delay(StartupGracePeriod, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Alert worker stopped.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await alertService.EvaluateAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Alert evaluation failed.");

                await eventService.WriteAsync(new OperationalEventRequest
                {
                    Severity = EventSeverity.Error,
                    Category = OperationalEventCategory.Alert,
                    Message = "Alert evaluation failed.",
                    Detail = exception.ToString()
                }, stoppingToken);
            }

            await Task.Delay(EvaluationInterval, stoppingToken);
        }

        logger.LogInformation("Alert worker stopped.");
    }
}
