using Microsoft.Extensions.Options;
using Relaywright.Web.Options;

namespace Relaywright.Web.Services.Updates;

public sealed class UpdateCheckWorker(
    IUpdateCheckService updateCheckService,
    IOptions<UpdateCheckOptions> options,
    ILogger<UpdateCheckWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Relaywright update checks are disabled.");
            return;
        }

        try
        {
            var startupDelay = options.Value.GetStartupDelay();
            if (startupDelay > TimeSpan.Zero)
            {
                await Task.Delay(startupDelay, stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                var status = await updateCheckService.RefreshAsync(stoppingToken);
                logger.LogInformation(
                    "Relaywright update check completed. State={State}; CurrentVersion={CurrentVersion}; LatestVersion={LatestVersion}; Repository={Repository}",
                    status.State,
                    status.CurrentVersion,
                    status.LatestVersion,
                    status.Repository);

                await Task.Delay(options.Value.GetInterval(), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Relaywright update check worker stopped after an unexpected failure.");
        }
    }
}
