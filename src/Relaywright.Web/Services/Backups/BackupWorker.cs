using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.Backups;

public sealed class BackupWorker(
    IBackupService backupService,
    BackupScheduleRepository scheduleRepository,
    IOperationalEventService eventService,
    ILogger<BackupWorker> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Backup worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var schedule = await backupService.GetScheduleAsync(stoppingToken);
                if (schedule.IsEnabled && IsDue(schedule.LastRunUtc, schedule.IntervalHours, clock.GetUtcNow()))
                {
                    var run = await backupService.CreateBackupAsync("system", scheduled: true, stoppingToken);
                    if (run.Status == Data.Entities.BackupRunStatus.Succeeded)
                    {
                        var completedUtc = clock.GetUtcNow();
                        await scheduleRepository.MarkRunCompletedAsync(completedUtc, stoppingToken);
                        await backupService.PruneByRetentionAsync(schedule.RetentionCount, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduled backup failed.");
                await eventService.WriteAsync(new OperationalEventRequest
                {
                    Severity = Data.Entities.EventSeverity.Error,
                    Category = Data.Entities.OperationalEventCategory.System,
                    Message = "Scheduled backup failed.",
                    Detail = exception.Message
                }, stoppingToken);
            }

            await Task.Delay(TimeSpan.FromMinutes(15), clock, stoppingToken);
        }

        logger.LogInformation("Backup worker stopped.");
    }

    private static bool IsDue(DateTimeOffset? lastRunUtc, int intervalHours, DateTimeOffset now)
    {
        return lastRunUtc is null || lastRunUtc.Value.AddHours(Math.Max(1, intervalHours)) <= now;
    }
}
