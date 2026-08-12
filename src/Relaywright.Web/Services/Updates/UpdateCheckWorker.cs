using Microsoft.Extensions.Options;
using Relaywright.Web.Options;

namespace Relaywright.Web.Services.Updates;

public sealed class UpdateCheckWorker(
    IUpdateCheckService updateCheckService,
    IOptions<UpdateCheckOptions> options,
    ILogger<UpdateCheckWorker> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

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
                await Task.Delay(startupDelay, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var status = await updateCheckService.RefreshAsync(stoppingToken);
                logger.LogInformation(
                    "Relaywright update check completed. State={State}; CurrentVersion={CurrentVersion}; LatestVersion={LatestVersion}; Repository={Repository}",
                    status.State,
                    status.CurrentVersion,
                    status.LatestVersion,
                    status.Repository);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Relaywright update check failed; the worker will retry after the configured interval.");
            }

            try
            {
                await Task.Delay(options.Value.GetInterval(), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
