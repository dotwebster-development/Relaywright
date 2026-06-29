using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Identity;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Relay;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class DataSeederTests
{
    [Fact]
    public async Task DevelopmentAllowsDefaultBootstrapPassword()
    {
        await using var fixture = await SeederFixture.CreateAsync();
        var seeder = fixture.CreateSeeder(
            Environments.Development,
            new BootstrapAdminOptions
            {
                UserName = "admin",
                Email = "admin@localhost",
                Password = BootstrapAdminOptions.DefaultDevelopmentPassword
            });

        await seeder.InitializeAsync(CancellationToken.None);

        Assert.True(await fixture.UserExistsAsync("admin"));
    }

    [Fact]
    public async Task ProductionRejectsCreatingAdminWithDefaultBootstrapPassword()
    {
        await using var fixture = await SeederFixture.CreateAsync();
        var seeder = fixture.CreateSeeder(
            Environments.Production,
            new BootstrapAdminOptions
            {
                UserName = "admin",
                Email = "admin@localhost",
                Password = BootstrapAdminOptions.DefaultDevelopmentPassword
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => seeder.InitializeAsync(CancellationToken.None));

        Assert.Contains("non-default", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductionAllowsEmptyBootstrapPasswordForFirstRunSetup()
    {
        await using var fixture = await SeederFixture.CreateAsync();
        var seeder = fixture.CreateSeeder(
            Environments.Production,
            new BootstrapAdminOptions
            {
                UserName = "admin",
                Email = "admin@localhost",
                Password = string.Empty
            });

        await seeder.InitializeAsync(CancellationToken.None);

        Assert.Equal(0, await fixture.GetUserCountAsync());
        Assert.Equal(1, await fixture.GetRelayConfigurationCountAsync());
        Assert.Equal(2, await fixture.GetTrustedNetworkCountAsync());
    }

    [Fact]
    public async Task ProductionRejectsExistingUserWithDefaultDevelopmentPassword()
    {
        await using var fixture = await SeederFixture.CreateAsync();
        await fixture.EnsureCreatedAsync();
        await fixture.CreateUserAsync("admin", BootstrapAdminOptions.DefaultDevelopmentPassword);
        var seeder = fixture.CreateSeeder(
            Environments.Production,
            new BootstrapAdminOptions
            {
                UserName = "admin",
                Email = "admin@localhost",
                Password = string.Empty
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => seeder.InitializeAsync(CancellationToken.None));

        Assert.Contains("default development password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InitializeIsIdempotentForBootstrapSeedData()
    {
        await using var fixture = await SeederFixture.CreateAsync();
        var seeder = fixture.CreateSeeder(
            Environments.Development,
            new BootstrapAdminOptions
            {
                UserName = "admin",
                Email = "admin@localhost",
                Password = BootstrapAdminOptions.DefaultDevelopmentPassword
            });

        await seeder.InitializeAsync(CancellationToken.None);
        await seeder.InitializeAsync(CancellationToken.None);

        Assert.Equal(1, await fixture.GetRelayConfigurationCountAsync());
        Assert.Equal(2, await fixture.GetTrustedNetworkCountAsync());
        Assert.Equal(1, await fixture.GetUserCountAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InitializeUpgradesBetaShapedDatabaseWithoutLosingExistingConfiguration()
    {
        await using var fixture = await SeederFixture.CreateAsync();
        await fixture.CreateBetaShapedDatabaseAsync();
        var seeder = fixture.CreateSeeder(
            Environments.Production,
            new BootstrapAdminOptions
            {
                UserName = "admin",
                Email = "admin@localhost",
                Password = string.Empty
            });

        await seeder.InitializeAsync(CancellationToken.None);

        var relayConfiguration = await fixture.GetRelayConfigurationAsync();
        Assert.Equal("beta.smtp.example.test", relayConfiguration.UpstreamHost);
        Assert.Equal(1, await fixture.GetTrustedNetworkCountAsync());
        Assert.Equal(0, await fixture.GetUserCountAsync());
        Assert.Equal(1, await fixture.GetRuntimeControlStateCountAsync());
        Assert.Equal(1, await fixture.GetSubmissionPolicyCountAsync());
        Assert.True(await fixture.GetAlertRuleCountAsync() > 0);
        Assert.True(await fixture.TableExistsAsync("ConfigurationSnapshots"));
        Assert.True(await fixture.TableExistsAsync("DiagnosticRuns"));
        Assert.True(await fixture.TableExistsAsync("DiagnosticStages"));

        AssertContainsColumn(await fixture.GetColumnNamesAsync("RelayConfigurations"), "UpstreamAuthenticationMode");
        AssertContainsColumn(await fixture.GetColumnNamesAsync("RelayConfigurations"), "MicrosoftTenantId");
        AssertContainsColumn(await fixture.GetColumnNamesAsync("RelayConfigurations"), "MicrosoftClientId");
        AssertContainsColumn(await fixture.GetColumnNamesAsync("RelayConfigurations"), "ProtectedMicrosoftClientSecret");
        AssertContainsColumn(await fixture.GetColumnNamesAsync("TrustedNetworks"), "Owner");
        AssertContainsColumn(await fixture.GetColumnNamesAsync("TrustedNetworks"), "RateLimitMessagesPerHour");
        AssertContainsColumn(await fixture.GetColumnNamesAsync("RuntimeControlStates"), "RestartRequired");
        AssertContainsColumn(await fixture.GetColumnNamesAsync("BackupRuns"), "IsEncrypted");
    }

    private static void AssertContainsColumn(ISet<string> columns, string columnName)
    {
        Assert.Contains(columnName, columns, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SeederFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _serviceProvider;

        private SeederFixture(SqliteConnection connection, ServiceProvider serviceProvider)
        {
            _connection = connection;
            _serviceProvider = serviceProvider;
        }

        public static async Task<SeederFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
            services
                .AddIdentity<ApplicationUser, IdentityRole>(options =>
                {
                    options.Password.RequiredLength = 12;
                    options.Password.RequireDigit = true;
                    options.Password.RequireUppercase = true;
                    options.Password.RequireLowercase = true;
                    options.Password.RequireNonAlphanumeric = false;
                })
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            return new SeederFixture(connection, services.BuildServiceProvider());
        }

        public DataSeeder CreateSeeder(string environmentName, BootstrapAdminOptions options)
        {
            return new DataSeeder(
                _serviceProvider,
                Microsoft.Extensions.Options.Options.Create(options),
                new TestHostEnvironment(environmentName),
                NullLogger<DataSeeder>.Instance);
        }

        public async Task EnsureCreatedAsync()
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }

        public async Task CreateBetaShapedDatabaseAsync()
        {
            await EnsureCreatedAsync();
            await using var scope = _serviceProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            await dbContext.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"DiagnosticStages\";");
            await dbContext.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"DiagnosticRuns\";");
            await dbContext.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"ConfigurationSnapshots\";");
            await dbContext.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"AlertResults\";");
            await dbContext.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"AlertRules\";");
            await dbContext.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"BackupScheduleStates\";");
            await dbContext.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"BackupRuns\";");
            await dbContext.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"SubmissionPolicies\";");
            await dbContext.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"RuntimeControlStates\";");
            await dbContext.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"TrustedNetworks\";");
            await dbContext.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"RelayConfigurations\";");

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE "RelayConfigurations" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_RelayConfigurations" PRIMARY KEY,
                    "ListenerBindAddress" TEXT NOT NULL,
                    "ListenerPort" INTEGER NOT NULL,
                    "ListenerHostName" TEXT NOT NULL,
                    "MaxMessageSizeBytes" INTEGER NOT NULL,
                    "EnableStartTls" INTEGER NOT NULL,
                    "CertificatePath" TEXT NULL,
                    "ProtectedCertificatePassword" TEXT NULL,
                    "UpstreamHost" TEXT NOT NULL,
                    "UpstreamPort" INTEGER NOT NULL,
                    "UpstreamSecureSocketOptions" INTEGER NOT NULL,
                    "UseUpstreamAuthentication" INTEGER NOT NULL,
                    "UpstreamUserName" TEXT NULL,
                    "ProtectedUpstreamPassword" TEXT NULL,
                    "UpstreamTimeoutSeconds" INTEGER NOT NULL,
                    "DeliveryConcurrency" INTEGER NOT NULL,
                    "MaxRetryCount" INTEGER NOT NULL,
                    "InitialRetryDelaySeconds" INTEGER NOT NULL,
                    "MaxRetryDelaySeconds" INTEGER NOT NULL,
                    "MessageExpirationHours" INTEGER NOT NULL,
                    "DeliveredRetentionHours" INTEGER NOT NULL,
                    "FailedRetentionHours" INTEGER NOT NULL,
                    "EventRetentionHours" INTEGER NOT NULL,
                    "UpdatedUtc" TEXT NOT NULL
                );
                """);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "RelayConfigurations" (
                    "Id",
                    "ListenerBindAddress",
                    "ListenerPort",
                    "ListenerHostName",
                    "MaxMessageSizeBytes",
                    "EnableStartTls",
                    "CertificatePath",
                    "ProtectedCertificatePassword",
                    "UpstreamHost",
                    "UpstreamPort",
                    "UpstreamSecureSocketOptions",
                    "UseUpstreamAuthentication",
                    "UpstreamUserName",
                    "ProtectedUpstreamPassword",
                    "UpstreamTimeoutSeconds",
                    "DeliveryConcurrency",
                    "MaxRetryCount",
                    "InitialRetryDelaySeconds",
                    "MaxRetryDelaySeconds",
                    "MessageExpirationHours",
                    "DeliveredRetentionHours",
                    "FailedRetentionHours",
                    "EventRetentionHours",
                    "UpdatedUtc")
                VALUES (
                    1,
                    '127.0.0.1',
                    2525,
                    'beta-relay',
                    1048576,
                    0,
                    NULL,
                    NULL,
                    'beta.smtp.example.test',
                    2525,
                    0,
                    0,
                    NULL,
                    NULL,
                    30,
                    1,
                    3,
                    30,
                    300,
                    24,
                    24,
                    168,
                    720,
                    '2026-01-01T00:00:00+00:00');
                """);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE "TrustedNetworks" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_TrustedNetworks" PRIMARY KEY AUTOINCREMENT,
                    "Cidr" TEXT NOT NULL,
                    "Description" TEXT NOT NULL,
                    "IsEnabled" INTEGER NOT NULL,
                    "CreatedUtc" TEXT NOT NULL,
                    "UpdatedUtc" TEXT NOT NULL
                );
                """);
            await dbContext.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX \"IX_TrustedNetworks_Cidr\" ON \"TrustedNetworks\" (\"Cidr\");");
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "TrustedNetworks" (
                    "Cidr",
                    "Description",
                    "IsEnabled",
                    "CreatedUtc",
                    "UpdatedUtc")
                VALUES (
                    '192.168.10.0/24',
                    'Beta lab subnet',
                    1,
                    '2026-01-01T00:00:00+00:00',
                    '2026-01-01T00:00:00+00:00');
                """);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE "RuntimeControlStates" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_RuntimeControlStates" PRIMARY KEY,
                    "IsDeliveryPaused" INTEGER NOT NULL,
                    "DeliveryPauseReason" TEXT NULL,
                    "DeliveryPausedBy" TEXT NULL,
                    "DeliveryPausedUtc" TEXT NULL,
                    "UpdatedUtc" TEXT NOT NULL
                );
                """);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE "BackupRuns" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_BackupRuns" PRIMARY KEY,
                    "StartedUtc" TEXT NOT NULL,
                    "CompletedUtc" TEXT NULL,
                    "Status" INTEGER NOT NULL,
                    "FileName" TEXT NULL,
                    "FileSizeBytes" INTEGER NULL,
                    "CreatedBy" TEXT NULL,
                    "Message" TEXT NULL,
                    "LastValidatedUtc" TEXT NULL,
                    "LastValidationSucceeded" INTEGER NULL,
                    "LastValidationMessage" TEXT NULL
                );
                """);
            await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
        }

        public async Task CreateUserAsync(string userName, string password)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var result = await userManager.CreateAsync(new ApplicationUser
            {
                UserName = userName,
                Email = $"{userName}@localhost",
                DisplayName = userName,
                EmailConfirmed = true
            }, password);

            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(x => x.Description)));
        }

        public async Task<bool> UserExistsAsync(string userName)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            return await userManager.FindByNameAsync(userName) is not null;
        }

        public async Task<int> GetRelayConfigurationCountAsync()
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await dbContext.RelayConfigurations.CountAsync();
        }

        public async Task<int> GetTrustedNetworkCountAsync()
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await dbContext.TrustedNetworks.CountAsync();
        }

        public async Task<RelayConfiguration> GetRelayConfigurationAsync()
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await dbContext.RelayConfigurations.SingleAsync();
        }

        public async Task<int> GetRuntimeControlStateCountAsync()
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await dbContext.RuntimeControlStates.CountAsync();
        }

        public async Task<int> GetSubmissionPolicyCountAsync()
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await dbContext.SubmissionPolicies.CountAsync();
        }

        public async Task<int> GetAlertRuleCountAsync()
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await dbContext.AlertRules.CountAsync();
        }

        public async Task<int> GetUserCountAsync()
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            return await userManager.Users.CountAsync();
        }

        public async Task<ISet<string>> GetColumnNamesAsync(string tableName)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var command = _connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }

            return columns;
        }

        public async Task<bool> TableExistsAsync(string tableName)
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
            command.Parameters.AddWithValue("$name", tableName);
            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result) == 1;
        }

        public async ValueTask DisposeAsync()
        {
            await _serviceProvider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Relaywright.Web.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
