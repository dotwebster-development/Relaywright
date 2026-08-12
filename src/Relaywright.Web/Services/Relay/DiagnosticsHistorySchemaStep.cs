using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;

namespace Relaywright.Web.Services.Relay;

public sealed class DiagnosticsHistorySchemaStep : ILegacySqliteSchemaStep
{
    public int Order => 400;
    public string Name => "Diagnostics and configuration history";

    public async Task ApplyAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "DiagnosticRuns" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_DiagnosticRuns" PRIMARY KEY, "Kind" INTEGER NOT NULL,
                "SessionId" TEXT NULL, "StartedUtc" TEXT NOT NULL, "CompletedUtc" TEXT NULL,
                "Succeeded" INTEGER NULL, "Message" TEXT NOT NULL, "RequestedBy" TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_DiagnosticRuns_Kind_StartedUtc" ON "DiagnosticRuns" ("Kind", "StartedUtc");
            CREATE TABLE IF NOT EXISTS "DiagnosticStages" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_DiagnosticStages" PRIMARY KEY AUTOINCREMENT,
                "DiagnosticRunId" TEXT NOT NULL, "Sequence" INTEGER NOT NULL, "Name" TEXT NOT NULL,
                "Status" INTEGER NOT NULL, "StartedUtc" TEXT NOT NULL, "CompletedUtc" TEXT NULL,
                "ElapsedMilliseconds" INTEGER NULL, "Message" TEXT NOT NULL, "Detail" TEXT NULL,
                CONSTRAINT "FK_DiagnosticStages_DiagnosticRuns_DiagnosticRunId" FOREIGN KEY ("DiagnosticRunId") REFERENCES "DiagnosticRuns" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_DiagnosticStages_DiagnosticRunId_Sequence" ON "DiagnosticStages" ("DiagnosticRunId", "Sequence");
            CREATE TABLE IF NOT EXISTS "ConfigurationSnapshots" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ConfigurationSnapshots" PRIMARY KEY, "Area" TEXT NOT NULL,
                "DisplayName" TEXT NOT NULL, "Summary" TEXT NOT NULL, "PayloadJson" TEXT NOT NULL,
                "CreatedBy" TEXT NULL, "CreatedUtc" TEXT NOT NULL, "IsRollback" INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_ConfigurationSnapshots_Area_CreatedUtc" ON "ConfigurationSnapshots" ("Area", "CreatedUtc");
            """,
            cancellationToken);
    }
}
