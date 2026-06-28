using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Identity;
using Relaywright.Web.Options;

namespace Relaywright.Web.Services.Relay;

public sealed class DataSeeder(
    IServiceProvider serviceProvider,
    IOptions<BootstrapAdminOptions> bootstrapAdminOptions,
    IHostEnvironment environment,
    ILogger<DataSeeder> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Initializing data store and bootstrap data.");

        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await UpgradeSchemaAsync(dbContext, cancellationToken);

        if (!await dbContext.RelayConfigurations.AnyAsync(cancellationToken))
        {
            dbContext.RelayConfigurations.Add(new RelayConfiguration());
            logger.LogInformation("Seeded default relay configuration.");
        }

        if (!await dbContext.TrustedNetworks.AnyAsync(cancellationToken))
        {
            dbContext.TrustedNetworks.AddRange(
                new TrustedNetwork
                {
                    Cidr = "127.0.0.1/32",
                    Description = "Localhost IPv4"
                },
                new TrustedNetwork
                {
                    Cidr = "::1/128",
                    Description = "Localhost IPv6"
                });
            logger.LogInformation("Seeded default localhost trusted networks.");
        }

        if (!await dbContext.RuntimeControlStates.AnyAsync(cancellationToken))
        {
            dbContext.RuntimeControlStates.Add(new RuntimeControlState());
            logger.LogInformation("Seeded runtime control state.");
        }

        if (!await dbContext.SubmissionPolicies.AnyAsync(cancellationToken))
        {
            dbContext.SubmissionPolicies.Add(new SubmissionPolicy());
            logger.LogInformation("Seeded default submission policy.");
        }

        if (!await dbContext.BackupScheduleStates.AnyAsync(cancellationToken))
        {
            dbContext.BackupScheduleStates.Add(new BackupScheduleState());
            logger.LogInformation("Seeded backup schedule state.");
        }

        await SeedAlertRulesAsync(dbContext, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await RejectDefaultPasswordsOutsideDevelopmentAsync(userManager, cancellationToken);

        if (string.IsNullOrWhiteSpace(bootstrapAdminOptions.Value.Password))
        {
            if (!await userManager.Users.AnyAsync(cancellationToken))
            {
                logger.LogWarning("No bootstrap admin password is configured. First-run admin setup is required through the web UI.");
            }
            else
            {
                logger.LogInformation("Bootstrap admin password is not configured. Automatic admin seeding skipped.");
            }

            logger.LogInformation("Data store initialization completed.");
            return;
        }

        ValidateBootstrapUserName();

        var existingAdmin = await userManager.FindByNameAsync(bootstrapAdminOptions.Value.UserName);
        if (existingAdmin is null)
        {
            ValidateBootstrapCreateOptions();

            var admin = new ApplicationUser
            {
                UserName = bootstrapAdminOptions.Value.UserName,
                Email = bootstrapAdminOptions.Value.Email,
                DisplayName = "Administrator",
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(admin, bootstrapAdminOptions.Value.Password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create bootstrap admin user: {string.Join("; ", result.Errors.Select(x => x.Description))}");
            }

            logger.LogWarning("Created bootstrap admin user '{UserName}'. Change the password immediately.", admin.UserName);
        }
        else
        {
            logger.LogInformation("Bootstrap admin user already exists. UserName={UserName}", bootstrapAdminOptions.Value.UserName);
        }

        logger.LogInformation("Data store initialization completed.");
    }

    private void ValidateBootstrapUserName()
    {
        if (string.IsNullOrWhiteSpace(bootstrapAdminOptions.Value.UserName))
        {
            throw new InvalidOperationException("Bootstrap admin user name is required.");
        }
    }

    private void ValidateBootstrapCreateOptions()
    {
        if (string.IsNullOrWhiteSpace(bootstrapAdminOptions.Value.Email))
        {
            throw new InvalidOperationException("Bootstrap admin email is required.");
        }

        if (!environment.IsDevelopment()
            && string.Equals(
                bootstrapAdminOptions.Value.Password,
                BootstrapAdminOptions.DefaultDevelopmentPassword,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Configure a non-default BootstrapAdmin password before starting outside Development.");
        }
    }

    private async Task RejectDefaultPasswordsOutsideDevelopmentAsync(
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        var users = await userManager.Users.ToListAsync(cancellationToken);
        foreach (var user in users)
        {
            if (string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                continue;
            }

            var result = userManager.PasswordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                BootstrapAdminOptions.DefaultDevelopmentPassword);

            if (result != PasswordVerificationResult.Failed)
            {
                throw new InvalidOperationException(
                    "An admin account still uses the default development password. Change it before starting outside Development.");
            }
        }
    }

    private async Task UpgradeSchemaAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        logger.LogInformation("Checking database schema upgrades.");

        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(\"RelayConfigurations\");";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                existingColumns.Add(reader.GetString(1));
            }
        }

        await AddColumnIfMissingAsync(
            dbContext,
            existingColumns,
            "UpstreamAuthenticationMode",
            "ALTER TABLE \"RelayConfigurations\" ADD COLUMN \"UpstreamAuthenticationMode\" INTEGER NOT NULL DEFAULT 0;",
            cancellationToken);
        await AddColumnIfMissingAsync(
            dbContext,
            existingColumns,
            "MicrosoftTenantId",
            "ALTER TABLE \"RelayConfigurations\" ADD COLUMN \"MicrosoftTenantId\" TEXT NULL;",
            cancellationToken);
        await AddColumnIfMissingAsync(
            dbContext,
            existingColumns,
            "MicrosoftClientId",
            "ALTER TABLE \"RelayConfigurations\" ADD COLUMN \"MicrosoftClientId\" TEXT NULL;",
            cancellationToken);
        await AddColumnIfMissingAsync(
            dbContext,
            existingColumns,
            "ProtectedMicrosoftClientSecret",
            "ALTER TABLE \"RelayConfigurations\" ADD COLUMN \"ProtectedMicrosoftClientSecret\" TEXT NULL;",
            cancellationToken);

        var trustedNetworkColumns = await GetColumnNamesAsync(dbContext, "TrustedNetworks", cancellationToken);
        await AddColumnIfMissingAsync(
            dbContext,
            trustedNetworkColumns,
            "Owner",
            "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"Owner\" TEXT NULL;",
            cancellationToken);
        await AddColumnIfMissingAsync(
            dbContext,
            trustedNetworkColumns,
            "Location",
            "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"Location\" TEXT NULL;",
            cancellationToken);
        await AddColumnIfMissingAsync(
            dbContext,
            trustedNetworkColumns,
            "AllowedSenderAddresses",
            "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"AllowedSenderAddresses\" TEXT NULL;",
            cancellationToken);
        await AddColumnIfMissingAsync(
            dbContext,
            trustedNetworkColumns,
            "BlockedSenderAddresses",
            "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"BlockedSenderAddresses\" TEXT NULL;",
            cancellationToken);
        await AddColumnIfMissingAsync(
            dbContext,
            trustedNetworkColumns,
            "AllowedRecipientDomains",
            "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"AllowedRecipientDomains\" TEXT NULL;",
            cancellationToken);
        await AddColumnIfMissingAsync(
            dbContext,
            trustedNetworkColumns,
            "BlockedRecipientDomains",
            "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"BlockedRecipientDomains\" TEXT NULL;",
            cancellationToken);
        await AddColumnIfMissingAsync(
            dbContext,
            trustedNetworkColumns,
            "MaxMessageSizeBytes",
            "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"MaxMessageSizeBytes\" INTEGER NULL;",
            cancellationToken);
        await AddColumnIfMissingAsync(
            dbContext,
            trustedNetworkColumns,
            "MaxRecipientsPerMessage",
            "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"MaxRecipientsPerMessage\" INTEGER NULL;",
            cancellationToken);
        await AddColumnIfMissingAsync(
            dbContext,
            trustedNetworkColumns,
            "RateLimitMessagesPerHour",
            "ALTER TABLE \"TrustedNetworks\" ADD COLUMN \"RateLimitMessagesPerHour\" INTEGER NULL;",
            cancellationToken);

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

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_QueuedMessages_Status_DeliveredUtc\" ON \"QueuedMessages\" (\"Status\", \"DeliveredUtc\");",
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_QueuedMessages_Status_LastAttemptCompletedUtc\" ON \"QueuedMessages\" (\"Status\", \"LastAttemptCompletedUtc\");",
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_QueuedMessages_ExpiresUtc\" ON \"QueuedMessages\" (\"ExpiresUtc\");",
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_QueuedMessageRecipients_QueuedMessageId_RecipientAddress\" ON \"QueuedMessageRecipients\" (\"QueuedMessageId\", \"RecipientAddress\");",
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_OperationalEvents_Severity_OccurredUtc\" ON \"OperationalEvents\" (\"Severity\", \"OccurredUtc\");",
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_OperationalEvents_Category_OccurredUtc\" ON \"OperationalEvents\" (\"Category\", \"OccurredUtc\");",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "RuntimeControlStates" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_RuntimeControlStates" PRIMARY KEY,
                "IsDeliveryPaused" INTEGER NOT NULL,
                "DeliveryPauseReason" TEXT NULL,
                "DeliveryPausedBy" TEXT NULL,
                "DeliveryPausedUtc" TEXT NULL,
                "UpdatedUtc" TEXT NOT NULL
            );
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "AlertRules" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AlertRules" PRIMARY KEY AUTOINCREMENT,
                "Key" TEXT NOT NULL,
                "DisplayName" TEXT NOT NULL,
                "Description" TEXT NOT NULL,
                "IsEnabled" INTEGER NOT NULL,
                "Threshold" INTEGER NOT NULL,
                "CooldownMinutes" INTEGER NOT NULL,
                "EmailRecipients" TEXT NULL,
                "IsActive" INTEGER NOT NULL,
                "LastTriggeredUtc" TEXT NULL,
                "LastResolvedUtc" TEXT NULL,
                "LastNotificationUtc" TEXT NULL,
                "LastNotificationSucceeded" INTEGER NULL,
                "LastNotificationMessage" TEXT NULL,
                "UpdatedUtc" TEXT NOT NULL
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_AlertRules_Key\" ON \"AlertRules\" (\"Key\");",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "AlertResults" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AlertResults" PRIMARY KEY AUTOINCREMENT,
                "AlertRuleId" INTEGER NOT NULL,
                "OccurredUtc" TEXT NOT NULL,
                "IsActive" INTEGER NOT NULL,
                "ObservedValue" INTEGER NOT NULL,
                "Threshold" INTEGER NOT NULL,
                "Message" TEXT NOT NULL,
                "NotificationSucceeded" INTEGER NULL,
                "NotificationMessage" TEXT NULL,
                CONSTRAINT "FK_AlertResults_AlertRules_AlertRuleId" FOREIGN KEY ("AlertRuleId") REFERENCES "AlertRules" ("Id") ON DELETE CASCADE
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_AlertResults_AlertRuleId_OccurredUtc\" ON \"AlertResults\" (\"AlertRuleId\", \"OccurredUtc\");",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "BackupRuns" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_BackupRuns" PRIMARY KEY,
                "StartedUtc" TEXT NOT NULL,
                "CompletedUtc" TEXT NULL,
                "Status" INTEGER NOT NULL,
                "FileName" TEXT NULL,
                "IsEncrypted" INTEGER NOT NULL DEFAULT 0,
                "FileSizeBytes" INTEGER NULL,
                "CreatedBy" TEXT NULL,
                "Message" TEXT NULL,
                "LastValidatedUtc" TEXT NULL,
                "LastValidationSucceeded" INTEGER NULL,
                "LastValidationMessage" TEXT NULL
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_BackupRuns_StartedUtc\" ON \"BackupRuns\" (\"StartedUtc\");",
            cancellationToken);
        await AddColumnIfMissingAsync(
            dbContext,
            await GetColumnNamesAsync(dbContext, "BackupRuns", cancellationToken),
            "IsEncrypted",
            "ALTER TABLE \"BackupRuns\" ADD COLUMN \"IsEncrypted\" INTEGER NOT NULL DEFAULT 0;",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "BackupScheduleStates" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_BackupScheduleStates" PRIMARY KEY,
                "IsEnabled" INTEGER NOT NULL,
                "IntervalHours" INTEGER NOT NULL,
                "RetentionCount" INTEGER NOT NULL,
                "LastRunUtc" TEXT NULL,
                "UpdatedUtc" TEXT NOT NULL
            );
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "DiagnosticRuns" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_DiagnosticRuns" PRIMARY KEY,
                "Kind" INTEGER NOT NULL,
                "SessionId" TEXT NULL,
                "StartedUtc" TEXT NOT NULL,
                "CompletedUtc" TEXT NULL,
                "Succeeded" INTEGER NULL,
                "Message" TEXT NOT NULL,
                "RequestedBy" TEXT NULL
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_DiagnosticRuns_Kind_StartedUtc\" ON \"DiagnosticRuns\" (\"Kind\", \"StartedUtc\");",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "DiagnosticStages" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_DiagnosticStages" PRIMARY KEY AUTOINCREMENT,
                "DiagnosticRunId" TEXT NOT NULL,
                "Sequence" INTEGER NOT NULL,
                "Name" TEXT NOT NULL,
                "Status" INTEGER NOT NULL,
                "StartedUtc" TEXT NOT NULL,
                "CompletedUtc" TEXT NULL,
                "ElapsedMilliseconds" INTEGER NULL,
                "Message" TEXT NOT NULL,
                "Detail" TEXT NULL,
                CONSTRAINT "FK_DiagnosticStages_DiagnosticRuns_DiagnosticRunId" FOREIGN KEY ("DiagnosticRunId") REFERENCES "DiagnosticRuns" ("Id") ON DELETE CASCADE
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_DiagnosticStages_DiagnosticRunId_Sequence\" ON \"DiagnosticStages\" (\"DiagnosticRunId\", \"Sequence\");",
            cancellationToken);

        if (closeConnection)
        {
            await connection.CloseAsync();
        }

        logger.LogInformation("Database schema upgrade check completed.");
    }

    private async Task AddColumnIfMissingAsync(
        ApplicationDbContext dbContext,
        ISet<string> existingColumns,
        string columnName,
        string sql,
        CancellationToken cancellationToken)
    {
        if (existingColumns.Contains(columnName))
        {
            logger.LogDebug("Database column already exists. Column={ColumnName}", columnName);
            return;
        }

        logger.LogInformation("Adding missing database column. Column={ColumnName}", columnName);
        await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        existingColumns.Add(columnName);
    }

    private static async Task<ISet<string>> GetColumnNamesAsync(
        ApplicationDbContext dbContext,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(1));
            }
        }

        if (closeConnection)
        {
            await connection.CloseAsync();
        }

        return columns;
    }

    private async Task SeedAlertRulesAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var existingKeys = await dbContext.AlertRules
            .Select(x => x.Key)
            .ToListAsync(cancellationToken);
        var existing = existingKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in GetDefaultAlertRules())
        {
            if (existing.Contains(rule.Key))
            {
                continue;
            }

            dbContext.AlertRules.Add(rule);
            logger.LogInformation("Seeded alert rule. Key={AlertRuleKey}; DisplayName={DisplayName}", rule.Key, rule.DisplayName);
        }
    }

    private static IReadOnlyList<AlertRule> GetDefaultAlertRules()
    {
        return
        [
            new AlertRule
            {
                Key = "queue-depth",
                DisplayName = "Queue depth",
                Description = "Active queue depth is above the configured threshold.",
                Threshold = 100,
                CooldownMinutes = 60
            },
            new AlertRule
            {
                Key = "oldest-active-message-minutes",
                DisplayName = "Oldest active message age",
                Description = "The oldest pending or retrying message is older than the configured minutes.",
                Threshold = 60,
                CooldownMinutes = 60
            },
            new AlertRule
            {
                Key = "failed-message-count",
                DisplayName = "Failed message count",
                Description = "Failed or expired messages are above the configured threshold.",
                Threshold = 10,
                CooldownMinutes = 60
            },
            new AlertRule
            {
                Key = "listener-down",
                DisplayName = "SMTP listener down",
                Description = "The SMTP listener is not reporting a running state.",
                Threshold = 1,
                CooldownMinutes = 15
            },
            new AlertRule
            {
                Key = "disk-free-mb",
                DisplayName = "Disk space low",
                Description = "Free space on the data volume is below the configured megabytes.",
                Threshold = 1024,
                CooldownMinutes = 60
            },
            new AlertRule
            {
                Key = "admin-certificate-expiry-days",
                DisplayName = "Admin certificate expiry",
                Description = "The configured admin HTTPS certificate expires within the configured days.",
                Threshold = 30,
                CooldownMinutes = 1440
            },
            new AlertRule
            {
                Key = "recent-upstream-failures",
                DisplayName = "Recent upstream failures",
                Description = "Recent delivery errors are above the configured threshold.",
                Threshold = 5,
                CooldownMinutes = 60
            }
        ];
    }
}
