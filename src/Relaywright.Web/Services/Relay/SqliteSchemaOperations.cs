using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;

namespace Relaywright.Web.Services.Relay;

internal static class SqliteSchemaOperations
{
    public static async Task AddColumnIfMissingAsync(
        ApplicationDbContext dbContext,
        ISet<string> existingColumns,
        string columnName,
        string sql,
        ILogger logger,
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

    public static async Task<ISet<string>> GetColumnNamesAsync(
        ApplicationDbContext dbContext,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }
}
