using Relaywright.Web.Infrastructure;
using Relaywright.Web.Options;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class DatabaseConfigurationTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void SqliteIsDefaultAndUsesAppDataDatabasePath()
    {
        var paths = Paths();

        var configuration = DatabaseConfiguration.Create(new DatabaseOptions(), paths);

        Assert.Equal(DatabaseProvider.Sqlite, configuration.Provider);
        Assert.True(configuration.IsSqlite);
        Assert.Contains(paths.DatabasePath, configuration.ConnectionString, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SqlServer", DatabaseProvider.SqlServer)]
    [InlineData("mssql", DatabaseProvider.SqlServer)]
    [InlineData("MySql", DatabaseProvider.MySql)]
    [InlineData("sqlite", DatabaseProvider.Sqlite)]
    [Trait("Category", "Unit")]
    public void ProviderNamesAreParsedCaseInsensitively(string value, DatabaseProvider expected)
    {
        Assert.Equal(expected, DatabaseConfiguration.ParseProvider(value));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ExternalProvidersRequireConnectionStrings()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseConfiguration.Create(
                new DatabaseOptions
                {
                    Provider = DatabaseProviderNames.SqlServer
                },
                Paths()));

        Assert.Contains("requires Database:ConnectionString", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ConnectionStringRedactionMasksSecretValues()
    {
        var redacted = DatabaseConfiguration.RedactConnectionString(
            "Server=db;Database=relaywright;User Id=relay;Password=s3cret;Application Name=Relaywright");

        Assert.Contains("Password=***", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("s3cret", redacted, StringComparison.Ordinal);
        Assert.Contains("User ID=relay", redacted, StringComparison.OrdinalIgnoreCase);
    }

    private static AppPaths Paths() => new(
        AppContext.BaseDirectory,
        new StorageOptions
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "relaywright-database-configuration-tests")
        });
}
