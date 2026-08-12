using Microsoft.Data.Sqlite;
using Relaywright.Web.Services.Backups;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class RestoredBackupValidatorTests
{
    [Fact]
    public async Task AcceptsDatabaseWithRequiredIdentityTableAndNonOverlappingNetworks()
    {
        using var appData = TempAppData.Create();
        var databasePath = await CreateDatabaseAsync(
            appData.Root,
            "10.0.0.0/24",
            "10.0.1.0/24");
        var validator = new RestoredBackupValidator();

        await validator.ValidateDatabaseAsync(databasePath, CancellationToken.None);
        await validator.ValidateTrustedNetworksAsync(databasePath, CancellationToken.None);
    }

    [Fact]
    public async Task RejectsInvalidTrustedNetworkCidr()
    {
        using var appData = TempAppData.Create();
        var databasePath = await CreateDatabaseAsync(appData.Root, "not-a-cidr");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RestoredBackupValidator().ValidateTrustedNetworksAsync(
                databasePath,
                CancellationToken.None));

        Assert.Contains("invalid trusted network CIDR", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> CreateDatabaseAsync(string root, params string[] cidrs)
    {
        var databasePath = Path.Combine(root, $"{Guid.NewGuid():N}.db");
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE \"AspNetUsers\" (\"Id\" TEXT); CREATE TABLE \"TrustedNetworks\" (\"Id\" INTEGER PRIMARY KEY, \"Cidr\" TEXT);";
            await command.ExecuteNonQueryAsync();
        }

        for (var index = 0; index < cidrs.Length; index++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO \"TrustedNetworks\" (\"Id\", \"Cidr\") VALUES ($id, $cidr);";
            command.Parameters.AddWithValue("$id", index + 1);
            command.Parameters.AddWithValue("$cidr", cidrs[index]);
            await command.ExecuteNonQueryAsync();
        }

        return databasePath;
    }
}
