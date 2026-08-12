using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Backups;

public sealed class BackupRunRepository(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<IReadOnlyList<BackupRun>> GetRecentAsync(
        int count,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var runs = await dbContext.BackupRuns
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return runs
            .OrderByDescending(x => x.StartedUtc)
            .Take(count)
            .ToList();
    }

    public async Task AddAsync(BackupRun run, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.BackupRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(BackupRun run, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.BackupRuns.Update(run);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<BackupRun?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.BackupRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<BackupRun?> FindLatestValidatedAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var runs = await dbContext.BackupRuns
            .AsNoTracking()
            .Where(x => x.Status == BackupRunStatus.Succeeded && x.LastValidationSucceeded == true)
            .ToListAsync(cancellationToken);
        return runs
            .OrderByDescending(x => x.LastValidatedUtc ?? x.CompletedUtc ?? x.StartedUtc)
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<BackupRun>> GetRetentionCandidatesAsync(
        int retentionCount,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var runs = await dbContext.BackupRuns
            .AsNoTracking()
            .Where(x => x.Status == BackupRunStatus.Succeeded)
            .ToListAsync(cancellationToken);
        return runs
            .OrderByDescending(x => x.StartedUtc)
            .Skip(Math.Clamp(retentionCount, 1, 100))
            .ToList();
    }

    public async Task UpdateRangeAsync(
        IReadOnlyCollection<BackupRun> runs,
        CancellationToken cancellationToken)
    {
        if (runs.Count == 0)
        {
            return;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.BackupRuns.UpdateRange(runs);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
