using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.Backups;

public sealed class BackupWorker(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IBackupService backupService,
    IOperationalEventService eventService,
    ILogger<BackupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Backup worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var schedule = await backupService.GetScheduleAsync(stoppingToken);
                if (schedule.IsEnabled && IsDue(schedule.LastRunUtc, schedule.IntervalHours))
                {
                    var run = await backupService.CreateBackupAsync("system", scheduled: true, stoppingToken);
                    if (run.Status == Data.Entities.BackupRunStatus.Succeeded)
                    {
                        await using var dbContext = await dbContextFactory.CreateDbContextAsync(stoppingToken);
                        var persisted = await dbContext.BackupScheduleStates.SingleAsync(x => x.Id == 1, stoppingToken);
                        persisted.LastRunUtc = DateTimeOffset.UtcNow;
                        persisted.UpdatedUtc = DateTimeOffset.UtcNow;
                        await dbContext.SaveChangesAsync(stoppingToken);
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

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }

        logger.LogInformation("Backup worker stopped.");
    }

    private static bool IsDue(DateTimeOffset? lastRunUtc, int intervalHours)
    {
        return lastRunUtc is null || lastRunUtc.Value.AddHours(Math.Max(1, intervalHours)) <= DateTimeOffset.UtcNow;
    }
}
