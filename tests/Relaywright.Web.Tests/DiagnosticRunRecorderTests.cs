using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Diagnostics;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class DiagnosticRunRecorderTests
{
    [Fact]
    public async Task RecordsRunAndOrderedStages()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var recorder = new DiagnosticRunRecorder(
            database.DbContextFactory,
            NullLogger<DiagnosticRunRecorder>.Instance);

        var run = await recorder.StartRunAsync(DiagnosticRunKind.Connectivity, null, "admin", CancellationToken.None);
        var stage = await recorder.StartStageAsync(run.Id, 1, "Connect/TLS", "Connecting.", CancellationToken.None);
        await recorder.CompleteStageAsync(stage.Id, DiagnosticStageStatus.Succeeded, "Connected.", null, CancellationToken.None);
        await recorder.CompleteRunAsync(run.Id, true, "Connectivity succeeded.", CancellationToken.None);

        var saved = await recorder.GetRunAsync(run.Id, CancellationToken.None);

        Assert.NotNull(saved);
        Assert.True(saved!.Succeeded);
        Assert.Single(saved.Stages);
        Assert.Equal("Connect/TLS", saved.Stages.Single().Name);
        Assert.Equal(DiagnosticStageStatus.Succeeded, saved.Stages.Single().Status);
        Assert.NotNull(saved.Stages.Single().ElapsedMilliseconds);
    }

    [Fact]
    public async Task GetRecentRunsOrdersByStartedUtcWithSqliteDateTimeOffsets()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var newest = Guid.NewGuid();
        var older = Guid.NewGuid();
        var oldest = Guid.NewGuid();
        await using (var dbContext = database.CreateDbContext())
        {
            dbContext.DiagnosticRuns.AddRange(
                new DiagnosticRun
                {
                    Id = oldest,
                    Kind = DiagnosticRunKind.Connectivity,
                    StartedUtc = now.AddHours(-3),
                    Message = "oldest"
                },
                new DiagnosticRun
                {
                    Id = newest,
                    Kind = DiagnosticRunKind.Connectivity,
                    StartedUtc = now,
                    Message = "newest"
                },
                new DiagnosticRun
                {
                    Id = older,
                    Kind = DiagnosticRunKind.Connectivity,
                    StartedUtc = now.AddHours(-1),
                    Message = "older"
                });
            await dbContext.SaveChangesAsync();
        }

        var recorder = new DiagnosticRunRecorder(
            database.DbContextFactory,
            NullLogger<DiagnosticRunRecorder>.Instance);

        var runs = await recorder.GetRecentRunsAsync(DiagnosticRunKind.Connectivity, 2, CancellationToken.None);

        Assert.Equal([newest, older], runs.Select(x => x.Id).ToArray());
    }
}
