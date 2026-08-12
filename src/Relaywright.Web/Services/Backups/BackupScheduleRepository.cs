using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Backups;

public sealed class BackupScheduleRepository(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<BackupScheduleState> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var schedule = await dbContext.BackupScheduleStates
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (schedule is not null)
        {
            return schedule;
        }

        schedule = new BackupScheduleState();
        dbContext.BackupScheduleStates.Add(schedule);
        await dbContext.SaveChangesAsync(cancellationToken);
        return schedule;
    }

    public async Task SaveAsync(BackupScheduleState schedule, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.BackupScheduleStates
            .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (existing is null)
        {
            existing = new BackupScheduleState();
            dbContext.BackupScheduleStates.Add(existing);
        }

        existing.IsEnabled = schedule.IsEnabled;
        existing.IntervalHours = schedule.IntervalHours;
        existing.RetentionCount = schedule.RetentionCount;
        existing.LastRunUtc = schedule.LastRunUtc;
        existing.UpdatedUtc = schedule.UpdatedUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkRunCompletedAsync(DateTimeOffset completedUtc, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var schedule = await dbContext.BackupScheduleStates.SingleAsync(x => x.Id == 1, cancellationToken);
        schedule.LastRunUtc = completedUtc;
        schedule.UpdatedUtc = completedUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
