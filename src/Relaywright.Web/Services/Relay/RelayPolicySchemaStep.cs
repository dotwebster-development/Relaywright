using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;

namespace Relaywright.Web.Services.Relay;

public sealed class RelayPolicySchemaStep(ILogger<RelayPolicySchemaStep> logger) : ILegacySqliteSchemaStep
{
    public int Order => 100;
    public string Name => "Relay and submission policy";

    public async Task ApplyAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var relayColumns = await SqliteSchemaOperations.GetColumnNamesAsync(dbContext, "RelayConfigurations", cancellationToken);
        await AddRelayAsync("UpstreamAuthenticationMode", "ALTER TABLE \"RelayConfigurations\" ADD COLUMN \"UpstreamAuthenticationMode\" INTEGER NOT NULL DEFAULT 0;");
        await AddRelayAsync("MicrosoftTenantId", "ALTER TABLE \"RelayConfigurations\" ADD COLUMN \"MicrosoftTenantId\" TEXT NULL;");
        await AddRelayAsync("MicrosoftClientId", "ALTER TABLE \"RelayConfigurations\" ADD COLUMN \"MicrosoftClientId\" TEXT NULL;");
        await AddRelayAsync("ProtectedMicrosoftClientSecret", "ALTER TABLE \"RelayConfigurations\" ADD COLUMN \"ProtectedMicrosoftClientSecret\" TEXT NULL;");

        var networkColumns = await SqliteSchemaOperations.GetColumnNamesAsync(dbContext, "TrustedNetworks", cancellationToken);
        await AddNetworkAsync("Owner", "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"Owner\" TEXT NULL;");
        await AddNetworkAsync("Location", "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"Location\" TEXT NULL;");
        await AddNetworkAsync("AllowedSenderAddresses", "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"AllowedSenderAddresses\" TEXT NULL;");
        await AddNetworkAsync("BlockedSenderAddresses", "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"BlockedSenderAddresses\" TEXT NULL;");
        await AddNetworkAsync("AllowedRecipientDomains", "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"AllowedRecipientDomains\" TEXT NULL;");
        await AddNetworkAsync("BlockedRecipientDomains", "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"BlockedRecipientDomains\" TEXT NULL;");
        await AddNetworkAsync("MaxMessageSizeBytes", "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"MaxMessageSizeBytes\" INTEGER NULL;");
        await AddNetworkAsync("MaxRecipientsPerMessage", "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"MaxRecipientsPerMessage\" INTEGER NULL;");
        await AddNetworkAsync("RateLimitMessagesPerHour", "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"RateLimitMessagesPerHour\" INTEGER NULL;");

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "SubmissionPolicies" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_SubmissionPolicies" PRIMARY KEY,
                "IsEnabled" INTEGER NOT NULL,
                "AllowedSenderAddresses" TEXT NULL,
                "BlockedSenderAddresses" TEXT NULL,
                "AllowedRecipientDomains" TEXT NULL,
                "BlockedRecipientDomains" TEXT NULL,
                "MaxMessageSizeBytes" INTEGER NULL,
                "MaxRecipientsPerMessage" INTEGER NULL,
                "UpdatedUtc" TEXT NOT NULL
            );
            """,
            cancellationToken);

        Task AddRelayAsync(string column, string sql) =>
            SqliteSchemaOperations.AddColumnIfMissingAsync(dbContext, relayColumns, column, sql, logger, cancellationToken);
        Task AddNetworkAsync(string column, string sql) =>
            SqliteSchemaOperations.AddColumnIfMissingAsync(dbContext, networkColumns, column, sql, logger, cancellationToken);
    }
}
