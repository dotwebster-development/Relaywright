using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;

namespace Relaywright.Web.Services.Relay;

public sealed class QueueRuntimeSchemaStep(ILogger<QueueRuntimeSchemaStep> logger) : ILegacySqliteSchemaStep
{
    public int Order => 200;
    public string Name => "Queue indexes and runtime control";

    public async Task ApplyAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var statements = new[]
        {
            "CREATE INDEX IF NOT EXISTS \"IX_QueuedMessages_Status_DeliveredUtc\" ON \"QueuedMessages\" (\"Status\", \"DeliveredUtc\");",
            "CREATE INDEX IF NOT EXISTS \"IX_QueuedMessages_Status_LastAttemptCompletedUtc\" ON \"QueuedMessages\" (\"Status\", \"LastAttemptCompletedUtc\");",
            "CREATE INDEX IF NOT EXISTS \"IX_QueuedMessages_ExpiresUtc\" ON \"QueuedMessages\" (\"ExpiresUtc\");",
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_QueuedMessageRecipients_QueuedMessageId_RecipientAddress\" ON \"QueuedMessageRecipients\" (\"QueuedMessageId\", \"RecipientAddress\");",
            "CREATE INDEX IF NOT EXISTS \"IX_OperationalEvents_Severity_OccurredUtc\" ON \"OperationalEvents\" (\"Severity\", \"OccurredUtc\");",
            "CREATE INDEX IF NOT EXISTS \"IX_OperationalEvents_Category_OccurredUtc\" ON \"OperationalEvents\" (\"Category\", \"OccurredUtc\");"
        };
        foreach (var statement in statements)
        {
            await dbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "RuntimeControlStates" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_RuntimeControlStates" PRIMARY KEY,
                "IsDeliveryPaused" INTEGER NOT NULL,
                "DeliveryPauseReason" TEXT NULL,
                "DeliveryPausedBy" TEXT NULL,
                "DeliveryPausedUtc" TEXT NULL,
                "RestartRequired" INTEGER NOT NULL DEFAULT 0,
                "RestartReason" TEXT NULL,
                "RestartRequestedBy" TEXT NULL,
                "RestartRequestedUtc" TEXT NULL,
                "RestartSupported" INTEGER NOT NULL DEFAULT 0,
                "UpdatedUtc" TEXT NOT NULL
            );
            """,
            cancellationToken);

        var columns = await SqliteSchemaOperations.GetColumnNamesAsync(dbContext, "RuntimeControlStates", cancellationToken);
        await AddAsync("RestartRequired", "ALTER TABLE \"RuntimeControlStates\" ADD COLUMN \"RestartRequired\" INTEGER NOT NULL DEFAULT 0;");
        await AddAsync("RestartReason", "ALTER TABLE \"RuntimeControlStates\" ADD COLUMN \"RestartReason\" TEXT NULL;");
        await AddAsync("RestartRequestedBy", "ALTER TABLE \"RuntimeControlStates\" ADD COLUMN \"RestartRequestedBy\" TEXT NULL;");
        await AddAsync("RestartRequestedUtc", "ALTER TABLE \"RuntimeControlStates\" ADD COLUMN \"RestartRequestedUtc\" TEXT NULL;");
        await AddAsync("RestartSupported", "ALTER TABLE \"RuntimeControlStates\" ADD COLUMN \"RestartSupported\" INTEGER NOT NULL DEFAULT 0;");

        Task AddAsync(string column, string sql) =>
            SqliteSchemaOperations.AddColumnIfMissingAsync(dbContext, columns, column, sql, logger, cancellationToken);
    }
}
