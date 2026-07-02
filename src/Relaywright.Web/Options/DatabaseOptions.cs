using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Infrastructure;

namespace Relaywright.Web.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Provider { get; set; } = DatabaseProviderNames.Sqlite;

    public string? ConnectionString { get; set; }
}

public enum DatabaseProvider
{
    Sqlite,
    SqlServer,
    MySql
}

public static class DatabaseProviderNames
{
    public const string Sqlite = "Sqlite";

    public const string SqlServer = "SqlServer";

    public const string MySql = "MySql";
}

public sealed class DatabaseConfiguration
{
    private DatabaseConfiguration(DatabaseProvider provider, string connectionString, string description)
    {
        Provider = provider;
        ConnectionString = connectionString;
        Description = description;
    }

    public DatabaseProvider Provider { get; }

    public string ConnectionString { get; }

    public string Description { get; }

    public bool IsSqlite => Provider == DatabaseProvider.Sqlite;

    public bool IsExternalServer => !IsSqlite;

    public static DatabaseConfiguration Create(DatabaseOptions options, AppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(appPaths);

        var provider = ParseProvider(options.Provider);
        var connectionString = string.IsNullOrWhiteSpace(options.ConnectionString)
            ? null
            : options.ConnectionString.Trim();

        return provider switch
        {
            DatabaseProvider.Sqlite => new DatabaseConfiguration(
                provider,
                connectionString ?? $"Data Source={appPaths.DatabasePath}",
                appPaths.DatabasePath),
            DatabaseProvider.SqlServer => new DatabaseConfiguration(
                provider,
                RequireConnectionString(connectionString, DatabaseProviderNames.SqlServer),
                "external SQL Server database"),
            DatabaseProvider.MySql => new DatabaseConfiguration(
                provider,
                RequireConnectionString(connectionString, DatabaseProviderNames.MySql),
                "external MySQL database"),
            _ => throw new InvalidOperationException($"Unsupported database provider '{provider}'.")
        };
    }

    public void Configure(DbContextOptionsBuilder options)
    {
        ArgumentNullException.ThrowIfNull(options);

        switch (Provider)
        {
            case DatabaseProvider.Sqlite:
                options.UseSqlite(ConnectionString);
                break;

            case DatabaseProvider.SqlServer:
                options.UseSqlServer(ConnectionString);
                break;

            case DatabaseProvider.MySql:
                options.UseMySQL(ConnectionString);
                break;

            default:
                throw new InvalidOperationException($"Unsupported database provider '{Provider}'.");
        }
    }

    public static DatabaseProvider ParseProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, DatabaseProviderNames.Sqlite, StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProvider.Sqlite;
        }

        if (string.Equals(value, DatabaseProviderNames.SqlServer, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Mssql", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProvider.SqlServer;
        }

        if (string.Equals(value, DatabaseProviderNames.MySql, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Mysql", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProvider.MySql;
        }

        throw new InvalidOperationException(
            $"Unsupported database provider '{value}'. Supported providers are Sqlite, SqlServer, and MySql.");
    }

    public static string RedactConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        try
        {
            var builder = new DbConnectionStringBuilder
            {
                ConnectionString = connectionString
            };

            foreach (var key in builder.Keys.Cast<string>().ToArray())
            {
                if (IsSecretKey(key))
                {
                    builder[key] = "***";
                }
            }

            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            return "Invalid connection string";
        }
    }

    private static string RequireConnectionString(string? connectionString, string provider)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"{provider} database provider requires Database:ConnectionString.");
        }

        return connectionString;
    }

    private static bool IsSecretKey(string key)
    {
        return key.Contains("password", StringComparison.OrdinalIgnoreCase)
            || key.Equals("pwd", StringComparison.OrdinalIgnoreCase)
            || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("token", StringComparison.OrdinalIgnoreCase);
    }
}
