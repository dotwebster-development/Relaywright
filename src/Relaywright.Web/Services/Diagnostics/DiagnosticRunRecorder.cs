using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Diagnostics;

public sealed class DiagnosticRunRecorder(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<DiagnosticRunRecorder> logger) : IDiagnosticRunRecorder
{
    public async Task<DiagnosticRun> StartRunAsync(
        DiagnosticRunKind kind,
        Guid? sessionId,
        string? requestedBy,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var run = new DiagnosticRun
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            SessionId = sessionId,
            StartedUtc = DateTimeOffset.UtcNow,
            RequestedBy = Trim(requestedBy, 256),
            Message = "Diagnostic run started."
        };

        dbContext.DiagnosticRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<DiagnosticStage> StartStageAsync(
        Guid runId,
        int sequence,
        string name,
        string message,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var stage = new DiagnosticStage
        {
            DiagnosticRunId = runId,
            Sequence = sequence,
            Name = Trim(name, 128) ?? string.Empty,
            Message = Trim(message, 2048) ?? string.Empty,
            StartedUtc = DateTimeOffset.UtcNow,
            Status = DiagnosticStageStatus.Running
        };

        dbContext.DiagnosticStages.Add(stage);
        await dbContext.SaveChangesAsync(cancellationToken);
        return stage;
    }

    public async Task CompleteStageAsync(
        long stageId,
        DiagnosticStageStatus status,
        string message,
        string? detail,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var stage = await dbContext.DiagnosticStages.SingleOrDefaultAsync(x => x.Id == stageId, cancellationToken);
        if (stage is null)
        {
            logger.LogWarning("Diagnostic stage completion skipped because stage was missing. StageId={StageId}", stageId);
            return;
        }

        var completed = DateTimeOffset.UtcNow;
        stage.Status = status;
        stage.CompletedUtc = completed;
        stage.ElapsedMilliseconds = Math.Max(0, (long)(completed - stage.StartedUtc).TotalMilliseconds);
        stage.Message = Trim(message, 2048) ?? string.Empty;
        stage.Detail = Trim(detail, 4096);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteRunAsync(Guid runId, bool succeeded, string message, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var run = await dbContext.DiagnosticRuns.SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null)
        {
            logger.LogWarning("Diagnostic run completion skipped because run was missing. RunId={RunId}", runId);
            return;
        }

        run.CompletedUtc = DateTimeOffset.UtcNow;
        run.Succeeded = succeeded;
        run.Message = Trim(message, 2048) ?? string.Empty;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DiagnosticRun>> GetRecentRunsAsync(
        DiagnosticRunKind? kind,
        int count,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.DiagnosticRuns
            .AsNoTracking()
            .Include(x => x.Stages)
            .AsQueryable();

        if (kind is not null)
        {
            query = query.Where(x => x.Kind == kind);
        }

        var loadedRuns = await query.ToListAsync(cancellationToken);
        var runs = loadedRuns
            .OrderByDescending(x => x.StartedUtc)
            .Take(Math.Max(1, count))
            .ToList();

        foreach (var run in runs)
        {
            run.Stages = run.Stages.OrderBy(x => x.Sequence).ToList();
        }

        return runs;
    }

    public async Task<DiagnosticRun?> GetRunAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var run = await dbContext.DiagnosticRuns
            .AsNoTracking()
            .Include(x => x.Stages)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (run is not null)
        {
            run.Stages = run.Stages.OrderBy(x => x.Sequence).ToList();
        }

        return run;
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
