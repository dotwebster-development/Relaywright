using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Data;
using Relaywright.Web.Services.Relay;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class DatabaseSchemaInitializerTests
{
    [Fact]
    public async Task FailedUpgradeIsRolledBackAndSuccessfulRerunRecordsVersion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var context = new ApplicationDbContext(options))
        {
            var failing = new DatabaseSchemaInitializer(
                NullLogger<DatabaseSchemaInitializer>.Instance,
                sqliteUpgrades: [new TestUpgrade(shouldFail: true)]);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => failing.InitializeAsync(context, CancellationToken.None));
        }

        Assert.Equal(0L, await ExecuteScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM \"SchemaVersions\";"));
        Assert.Equal(0L, await ExecuteScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'UpgradeProbe';"));

        await using (var context = new ApplicationDbContext(options))
        {
            var recovered = new DatabaseSchemaInitializer(
                NullLogger<DatabaseSchemaInitializer>.Instance,
                sqliteUpgrades: [new TestUpgrade(shouldFail: false)]);
            await recovered.InitializeAsync(context, CancellationToken.None);
        }

        Assert.Equal(1L, await ExecuteScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM \"SchemaVersions\" WHERE \"Version\" = 1;"));
        Assert.Equal(1L, await ExecuteScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'UpgradeProbe';"));
    }

    [Fact]
    public void UpgradeVersionsMustBeContiguousAndStartAtOne()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new DatabaseSchemaInitializer(
                NullLogger<DatabaseSchemaInitializer>.Instance,
                sqliteUpgrades: [new VersionTwoUpgrade()]));

        Assert.Contains("contiguous versions", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyBaselineExecutesNamedStepsInOrderAndRestoresConnectionState()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new ApplicationDbContext(options);
        var applied = new List<string>();
        var upgrade = new LegacyBaselineSqliteSchemaUpgrade(
            NullLogger<LegacyBaselineSqliteSchemaUpgrade>.Instance,
            [
                new RecordingLegacyStep(200, "second", applied),
                new RecordingLegacyStep(100, "first", applied)
            ]);

        await upgrade.ApplyAsync(context, CancellationToken.None);

        Assert.Equal(["first", "second"], applied);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void LegacyBaselineRejectsDuplicateStepOrder()
    {
        var applied = new List<string>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new LegacyBaselineSqliteSchemaUpgrade(
                NullLogger<LegacyBaselineSqliteSchemaUpgrade>.Instance,
                [
                    new RecordingLegacyStep(100, "one", applied),
                    new RecordingLegacyStep(100, "two", applied)
                ]));

        Assert.Contains("unique order", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<T> ExecuteScalarAsync<T>(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The scalar query returned null.");
        return (T)Convert.ChangeType(result, typeof(T), CultureInfo.InvariantCulture);
    }

    private sealed class TestUpgrade(bool shouldFail) : ISqliteSchemaUpgrade
    {
        public int Version => 1;

        public async Task ApplyAsync(
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken)
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                "CREATE TABLE \"UpgradeProbe\" (\"Id\" INTEGER NOT NULL);",
                cancellationToken);
            if (shouldFail)
            {
                throw new InvalidOperationException("Injected upgrade failure.");
            }
        }
    }

    private sealed class VersionTwoUpgrade : ISqliteSchemaUpgrade
    {
        public int Version => 2;

        public Task ApplyAsync(
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingLegacyStep(
        int order,
        string name,
        ICollection<string> applied) : ILegacySqliteSchemaStep
    {
        public int Order => order;

        public string Name => name;

        public Task ApplyAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
        {
            applied.Add(name);
            return Task.CompletedTask;
        }
    }
}
