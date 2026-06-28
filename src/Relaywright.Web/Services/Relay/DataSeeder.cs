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

        await dbContext.SaveChangesAsync(cancellationToken);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ValidateBootstrapUserName();
        await RejectDefaultPasswordsOutsideDevelopmentAsync(userManager, cancellationToken);

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

        if (string.IsNullOrWhiteSpace(bootstrapAdminOptions.Value.Password))
        {
            throw new InvalidOperationException("Bootstrap admin password is required.");
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
}
