using Microsoft.Data.Sqlite;

namespace Relaywright.Web.Services.Backups;

internal static class BackupCredentialSanitizer
{
    public static async Task SanitizeAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken);

        foreach (var tableName in new[]
                 {
                     "AspNetUserTokens",
                     "AspNetUserClaims",
                     "AspNetUserLogins",
                     "AspNetUserRoles",
                     "AspNetUsers"
                 })
        {
            if (await TableExistsAsync(connection, tableName, cancellationToken))
            {
                await ExecuteNonQueryAsync(connection, $"DELETE FROM {QuoteIdentifier(tableName)};", cancellationToken);
            }
        }

        if (await TableExistsAsync(connection, "RelayConfigurations", cancellationToken))
        {
            var relayColumns = await GetColumnNamesAsync(connection, "RelayConfigurations", cancellationToken);
            var assignments = new List<string>();

            AddNullAssignmentIfPresent(assignments, relayColumns, "ProtectedCertificatePassword");
            AddNullAssignmentIfPresent(assignments, relayColumns, "ProtectedUpstreamPassword");
            AddNullAssignmentIfPresent(assignments, relayColumns, "ProtectedMicrosoftClientSecret");
            AddLiteralAssignmentIfPresent(assignments, relayColumns, "UseUpstreamAuthentication", "0");

            if (assignments.Count > 0)
            {
                await ExecuteNonQueryAsync(
                    connection,
                    $"UPDATE \"RelayConfigurations\" SET {string.Join(", ", assignments)};",
                    cancellationToken);
            }
        }

        if (await TableExistsAsync(connection, "ConfigurationSnapshots", cancellationToken))
        {
            await ExecuteNonQueryAsync(connection, "DELETE FROM \"ConfigurationSnapshots\";", cancellationToken);
        }

        foreach (var tableName in new[]
                 {
                     "OperationalEvents",
                     "DiagnosticStages",
                     "DiagnosticRuns",
                     "AlertResults",
                     "BackupRuns"
                 })
        {
            if (await TableExistsAsync(connection, tableName, cancellationToken))
            {
                await ExecuteNonQueryAsync(connection, $"DELETE FROM {QuoteIdentifier(tableName)};", cancellationToken);
            }
        }

        await ClearColumnsIfPresentAsync(
            connection,
            "QueuedMessages",
            ["LastResponseText", "LastError"],
            cancellationToken);
        await ClearColumnsIfPresentAsync(
            connection,
            "DeliveryAttempts",
            ["ResponseText", "ExceptionMessage"],
            cancellationToken);
        await ClearColumnsIfPresentAsync(
            connection,
            "AlertRules",
            ["LastNotificationMessage"],
            cancellationToken);
    }

    private static void AddNullAssignmentIfPresent(
        List<string> assignments,
        ISet<string> columns,
        string columnName)
    {
        AddLiteralAssignmentIfPresent(assignments, columns, columnName, "NULL");
    }

    private static void AddLiteralAssignmentIfPresent(
        List<string> assignments,
        ISet<string> columns,
        string columnName,
        string value)
    {
        if (columns.Contains(columnName))
        {
            assignments.Add($"{QuoteIdentifier(columnName)} = {value}");
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

    private static async Task<ISet<string>> GetColumnNamesAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static async Task ClearColumnsIfPresentAsync(
        SqliteConnection connection,
        string tableName,
        IReadOnlyList<string> columnNames,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, tableName, cancellationToken))
        {
            return;
        }

        var existingColumns = await GetColumnNamesAsync(connection, tableName, cancellationToken);
        var assignments = columnNames
            .Where(existingColumns.Contains)
            .Select(columnName => $"{QuoteIdentifier(columnName)} = NULL")
            .ToList();
        if (assignments.Count == 0)
        {
            return;
        }

        await ExecuteNonQueryAsync(
            connection,
            $"UPDATE {QuoteIdentifier(tableName)} SET {string.Join(", ", assignments)};",
            cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string QuoteIdentifier(string value)
    {
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
