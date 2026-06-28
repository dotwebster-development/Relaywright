using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Relaywright.Web.Data;
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

        public async Task<int> GetUserCountAsync()
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            return await userManager.Users.CountAsync();
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
