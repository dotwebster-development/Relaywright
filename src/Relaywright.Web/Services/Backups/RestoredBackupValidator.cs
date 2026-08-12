using Microsoft.Data.Sqlite;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Services.Backups;

public sealed class RestoredBackupValidator
{
    public async Task ValidateDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(
            $"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"AspNetUsers\";";
        _ = await command.ExecuteScalarAsync(cancellationToken);
    }

    public async Task ValidateTrustedNetworksAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(
            $"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken);
        if (!await TableExistsAsync(connection, "TrustedNetworks", cancellationToken))
        {
            return;
        }

        var networks = new List<TrustedNetworkCidr>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT \"Id\", \"Cidr\" FROM \"TrustedNetworks\";";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt32(0);
                var cidr = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                if (!CidrRange.TryParse(cidr, out var range))
                {
                    throw new InvalidOperationException(
                        $"Restored backup contains an invalid trusted network CIDR: {cidr}");
                }

                networks.Add(new TrustedNetworkCidr(id, cidr, range!));
            }
        }

        for (var index = 0; index < networks.Count; index++)
        {
            for (var comparisonIndex = index + 1; comparisonIndex < networks.Count; comparisonIndex++)
            {
                if (networks[index].Range.Overlaps(networks[comparisonIndex].Range))
                {
                    throw new InvalidOperationException(
                        $"Restored backup contains overlapping trusted networks: {networks[index].Cidr} and {networks[comparisonIndex].Cidr}.");
                }
            }
        }
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", tableName);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private sealed record TrustedNetworkCidr(int Id, string Cidr, CidrRange Range);
}
