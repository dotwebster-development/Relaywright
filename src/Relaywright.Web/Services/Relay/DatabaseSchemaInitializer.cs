using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Data;
using Relaywright.Web.Identity;
using Relaywright.Web.Options;

namespace Relaywright.Web.Services.Relay;

public sealed class DatabaseSchemaInitializer
{
    public const int CurrentSqliteSchemaVersion = 1;

    private readonly ILogger<DatabaseSchemaInitializer> logger;
    private readonly DatabaseConfiguration? databaseConfiguration;
    private readonly IReadOnlyList<ISqliteSchemaUpgrade> sqliteUpgrades;
    private readonly TimeProvider clock;

    public DatabaseSchemaInitializer(
        ILogger<DatabaseSchemaInitializer> logger,
        DatabaseConfiguration? databaseConfiguration = null,
        IEnumerable<ISqliteSchemaUpgrade>? sqliteUpgrades = null,
        TimeProvider? timeProvider = null)
    {
        this.logger = logger;
        this.databaseConfiguration = databaseConfiguration;
        this.clock = timeProvider ?? TimeProvider.System;
        this.sqliteUpgrades = (sqliteUpgrades ??
            [
                new LegacyBaselineSqliteSchemaUpgrade(
                    NullLogger<LegacyBaselineSqliteSchemaUpgrade>.Instance)
            ])
            .OrderBy(x => x.Version)
            .ToArray();
        ValidateUpgradeSequence(this.sqliteUpgrades);
    }

    public async Task InitializeAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        if (databaseConfiguration is null || databaseConfiguration.IsSqlite)
        {
            await InitializeSqliteSchemaAsync(dbContext, cancellationToken);
            return;
        }

        await InitializeServerSchemaAsync(dbContext, cancellationToken);
    }

    private async Task InitializeSqliteSchemaAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await EnsureSchemaVersionTableAsync(dbContext, cancellationToken);
            var appliedVersion = await GetAppliedSchemaVersionAsync(dbContext, cancellationToken);
            if (appliedVersion > CurrentSqliteSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"The SQLite database schema version {appliedVersion} is newer than this Relaywright version supports ({CurrentSqliteSchemaVersion}).");
            }

            foreach (var upgrade in sqliteUpgrades.Where(x => x.Version > appliedVersion))
            {
                logger.LogInformation(
                    "Applying SQLite schema upgrade. FromVersion={FromVersion}; ToVersion={ToVersion}",
                    appliedVersion,
                    upgrade.Version);
                await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                await upgrade.ApplyAsync(dbContext, cancellationToken);
                await RecordSchemaVersionAsync(dbContext, upgrade.Version, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                appliedVersion = upgrade.Version;
            }

            logger.LogInformation("Database schema is current. Version={SchemaVersion}", appliedVersion);
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task InitializeServerSchemaAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await dbContext.Database.EnsureCreatedAsync(cancellationToken);
            if (created)
            {
                logger.LogInformation(
                    "Created Relaywright schema in configured {DatabaseProvider} database.",
                    databaseConfiguration!.Provider);
                return;
            }

            _ = await dbContext.Set<ApplicationUser>().AsNoTracking().AnyAsync(cancellationToken);
            _ = await dbContext.RelayConfigurations.AsNoTracking().AnyAsync(cancellationToken);
            _ = await dbContext.QueuedMessages.AsNoTracking().AnyAsync(cancellationToken);
            _ = await dbContext.OperationalEvents.AsNoTracking().AnyAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"The configured {databaseConfiguration!.Provider} database could not be initialized. Use an empty database for new SQL Server/MySQL installs, or an existing database that already contains the current Relaywright schema. SQLite-to-server migration is not supported in this release.",
                exception);
        }
    }

    private static Task EnsureSchemaVersionTableAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "SchemaVersions" (
                "Version" INTEGER NOT NULL CONSTRAINT "PK_SchemaVersions" PRIMARY KEY,
                "AppliedUtc" TEXT NOT NULL
            );
            """,
            cancellationToken);
    }

    private static async Task<int> GetAppliedSchemaVersionAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(\"Version\"), 0) FROM \"SchemaVersions\";";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private Task RecordSchemaVersionAsync(
        ApplicationDbContext dbContext,
        int version,
        CancellationToken cancellationToken)
    {
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "SchemaVersions" ("Version", "AppliedUtc")
            VALUES ({version}, {clock.GetUtcNow()});
            """,
            cancellationToken);
    }

    private static void ValidateUpgradeSequence(IReadOnlyList<ISqliteSchemaUpgrade> upgrades)
    {
        if (upgrades.Count == 0 ||
            upgrades[0].Version != 1 ||
            upgrades[^1].Version != CurrentSqliteSchemaVersion ||
            upgrades.Select(x => x.Version).Distinct().Count() != upgrades.Count ||
            upgrades.Where((upgrade, index) => upgrade.Version != index + 1).Any())
        {
            throw new InvalidOperationException(
                "SQLite schema upgrades must have unique, contiguous versions beginning at version 1.");
        }
    }
}
