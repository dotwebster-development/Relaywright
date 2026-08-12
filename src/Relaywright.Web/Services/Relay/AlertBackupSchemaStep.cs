using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;

namespace Relaywright.Web.Services.Relay;

public sealed class AlertBackupSchemaStep(ILogger<AlertBackupSchemaStep> logger) : ILegacySqliteSchemaStep
{
    public int Order => 300;
    public string Name => "Alerts and backups";

    public async Task ApplyAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "AlertRules" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AlertRules" PRIMARY KEY AUTOINCREMENT,
                "Key" TEXT NOT NULL, "DisplayName" TEXT NOT NULL, "Description" TEXT NOT NULL,
                "IsEnabled" INTEGER NOT NULL, "Threshold" INTEGER NOT NULL, "CooldownMinutes" INTEGER NOT NULL,
                "EmailRecipients" TEXT NULL, "IsActive" INTEGER NOT NULL, "LastTriggeredUtc" TEXT NULL,
                "LastResolvedUtc" TEXT NULL, "LastNotificationUtc" TEXT NULL, "LastNotificationSucceeded" INTEGER NULL,
                "LastNotificationMessage" TEXT NULL, "UpdatedUtc" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AlertRules_Key" ON "AlertRules" ("Key");
            CREATE TABLE IF NOT EXISTS "AlertResults" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AlertResults" PRIMARY KEY AUTOINCREMENT,
                "AlertRuleId" INTEGER NOT NULL, "OccurredUtc" TEXT NOT NULL, "IsActive" INTEGER NOT NULL,
                "ObservedValue" INTEGER NOT NULL, "Threshold" INTEGER NOT NULL, "Message" TEXT NOT NULL,
                "NotificationSucceeded" INTEGER NULL, "NotificationMessage" TEXT NULL,
                CONSTRAINT "FK_AlertResults_AlertRules_AlertRuleId" FOREIGN KEY ("AlertRuleId") REFERENCES "AlertRules" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_AlertResults_AlertRuleId_OccurredUtc" ON "AlertResults" ("AlertRuleId", "OccurredUtc");
            CREATE TABLE IF NOT EXISTS "BackupRuns" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_BackupRuns" PRIMARY KEY, "StartedUtc" TEXT NOT NULL,
                "CompletedUtc" TEXT NULL, "Status" INTEGER NOT NULL, "FileName" TEXT NULL,
                "IsEncrypted" INTEGER NOT NULL DEFAULT 0, "FileSizeBytes" INTEGER NULL, "CreatedBy" TEXT NULL,
                "Message" TEXT NULL, "LastValidatedUtc" TEXT NULL, "LastValidationSucceeded" INTEGER NULL,
                "LastValidationMessage" TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_BackupRuns_StartedUtc" ON "BackupRuns" ("StartedUtc");
            CREATE TABLE IF NOT EXISTS "BackupScheduleStates" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_BackupScheduleStates" PRIMARY KEY,
                "IsEnabled" INTEGER NOT NULL, "IntervalHours" INTEGER NOT NULL, "RetentionCount" INTEGER NOT NULL,
                "LastRunUtc" TEXT NULL, "UpdatedUtc" TEXT NOT NULL
            );
            """,
            cancellationToken);

        var columns = await SqliteSchemaOperations.GetColumnNamesAsync(dbContext, "BackupRuns", cancellationToken);
        await SqliteSchemaOperations.AddColumnIfMissingAsync(
            dbContext, columns, "IsEncrypted",
            "ALTER TABLE \"BackupRuns\" ADD COLUMN \"IsEncrypted\" INTEGER NOT NULL DEFAULT 0;",
            logger, cancellationToken);
    }
}
