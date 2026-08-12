using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Data;

namespace Relaywright.Web.Services.Relay;

public sealed class LegacyBaselineSqliteSchemaUpgrade : ISqliteSchemaUpgrade
{
    private readonly ILogger<LegacyBaselineSqliteSchemaUpgrade> logger;
    private readonly IReadOnlyList<ILegacySqliteSchemaStep> steps;

    public LegacyBaselineSqliteSchemaUpgrade(
        ILogger<LegacyBaselineSqliteSchemaUpgrade> logger,
        IEnumerable<ILegacySqliteSchemaStep>? steps = null)
    {
        this.logger = logger;
        var configuredSteps = steps?.ToArray() ?? [];
        this.steps = (configuredSteps.Length > 0 ? configuredSteps : CreateDefaultSteps())
            .OrderBy(x => x.Order)
            .ToArray();

        if (this.steps.Select(x => x.Order).Distinct().Count() != this.steps.Count)
        {
            throw new InvalidOperationException("Legacy SQLite schema steps must have unique order values.");
        }
    }

    public int Version => 1;

    public async Task ApplyAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            foreach (var step in steps)
            {
                logger.LogDebug(
                    "Applying legacy SQLite schema step. Order={Order}; Name={Name}",
                    step.Order,
                    step.Name);
                await step.ApplyAsync(dbContext, cancellationToken);
            }
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }

        logger.LogInformation("Applied legacy SQLite schema convergence.");
    }

    private static ILegacySqliteSchemaStep[] CreateDefaultSteps()
    {
        return
        [
            new RelayPolicySchemaStep(NullLogger<RelayPolicySchemaStep>.Instance),
            new QueueRuntimeSchemaStep(NullLogger<QueueRuntimeSchemaStep>.Instance),
            new AlertBackupSchemaStep(NullLogger<AlertBackupSchemaStep>.Instance),
            new DiagnosticsHistorySchemaStep()
        ];
    }
}
