using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Relaywright.Web.Infrastructure;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class RelaywrightStartupOptionsTests
{
    [Fact]
    public void DefaultsAreValid()
    {
        var options = RelaywrightStartupOptions.Load(BuildConfiguration([]));

        Assert.Equal("App_Data", options.Storage.DataDirectory);
        Assert.Equal(15, options.QueueProcessing.StaleClaimMinutes);
    }

    [Theory]
    [InlineData("QueueProcessing:StaleClaimMinutes", "0", "QueueProcessing:StaleClaimMinutes")]
    [InlineData("Storage:SpoolDirectoryName", "../spool", "Storage:SpoolDirectoryName")]
    [InlineData("Database:Provider", "SqlServer", "Database:ConnectionString")]
    [InlineData("UpdateCheck:IntervalHours", "0", "UpdateCheck:IntervalHours")]
    public void InvalidStartupConfigurationFailsBeforeServicesStart(
        string key,
        string value,
        string expectedFailure)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [key] = value
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            RelaywrightStartupOptions.Load(configuration));

        Assert.Contains(exception.Failures, failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
    }

    private static IConfiguration BuildConfiguration(IEnumerable<KeyValuePair<string, string?>> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
