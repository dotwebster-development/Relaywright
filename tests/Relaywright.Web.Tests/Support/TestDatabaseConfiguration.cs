using Relaywright.Web.Infrastructure;
using Relaywright.Web.Options;

namespace Relaywright.Web.Tests.Support;

internal static class TestDatabaseConfiguration
{
    private static readonly AppPaths Paths = new(
        AppContext.BaseDirectory,
        new StorageOptions
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "relaywright-test-database-options")
        });

    public static DatabaseConfiguration Sqlite { get; } = DatabaseConfiguration.Create(
        new DatabaseOptions
        {
            Provider = DatabaseProviderNames.Sqlite,
            ConnectionString = "Data Source=:memory:"
        },
        Paths);

    public static DatabaseConfiguration SqlServer(string connectionString) => DatabaseConfiguration.Create(
        new DatabaseOptions
        {
            Provider = DatabaseProviderNames.SqlServer,
            ConnectionString = connectionString
        },
        Paths);

    public static DatabaseConfiguration MySql(string connectionString) => DatabaseConfiguration.Create(
        new DatabaseOptions
        {
            Provider = DatabaseProviderNames.MySql,
            ConnectionString = connectionString
        },
        Paths);
}
