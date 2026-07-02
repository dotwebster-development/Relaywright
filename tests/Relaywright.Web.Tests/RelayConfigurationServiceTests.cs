using MailKit.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Services.Security;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class RelayConfigurationServiceTests
{
    [Theory]
    [InlineData(SecureSocketOptions.StartTls)]
    [InlineData(SecureSocketOptions.SslOnConnect)]
    public async Task SaveAllowsAuthenticationWithGuaranteedTls(SecureSocketOptions secureSocketOptions)
    {
        await using var fixture = await RelayConfigurationFixture.CreateAsync();
        var model = CreateAuthenticatedModel(secureSocketOptions);

        await fixture.Service.SaveAsync(model, CancellationToken.None);

        var saved = await fixture.GetConfigurationAsync();
        Assert.True(saved.UseUpstreamAuthentication);
        Assert.Equal(secureSocketOptions, saved.UpstreamSecureSocketOptions);
    }

    [Theory]
    [InlineData(SecureSocketOptions.None)]
    [InlineData(SecureSocketOptions.Auto)]
    [InlineData(SecureSocketOptions.StartTlsWhenAvailable)]
    public async Task SaveRejectsAuthenticationWithoutGuaranteedTls(SecureSocketOptions secureSocketOptions)
    {
        await using var fixture = await RelayConfigurationFixture.CreateAsync();
        var model = CreateAuthenticatedModel(secureSocketOptions);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.SaveAsync(model, CancellationToken.None));

        Assert.Contains("guaranteed TLS", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAllowsNoAuthenticationWithNonTlsUpstreamMode()
    {
        await using var fixture = await RelayConfigurationFixture.CreateAsync();
        var model = CreateBaseModel();
        model.UpstreamSecureSocketOptions = SecureSocketOptions.None;
        model.UseUpstreamAuthentication = false;
        model.UpstreamAuthenticationMode = null;

        await fixture.Service.SaveAsync(model, CancellationToken.None);

        var saved = await fixture.GetConfigurationAsync();
        Assert.False(saved.UseUpstreamAuthentication);
        Assert.Equal(SecureSocketOptions.None, saved.UpstreamSecureSocketOptions);
    }

    private static RelayConfigurationEditModel CreateAuthenticatedModel(SecureSocketOptions secureSocketOptions)
    {
        var model = CreateBaseModel();
        model.UpstreamSecureSocketOptions = secureSocketOptions;
        model.UseUpstreamAuthentication = true;
        model.UpstreamAuthenticationMode = UpstreamAuthenticationMode.Basic;
        model.UpstreamUserName = "relay@example.test";
        model.UpstreamPassword = "Password12345";
        return model;
    }

    private static RelayConfigurationEditModel CreateBaseModel()
    {
        return new RelayConfigurationEditModel
        {
            ListenerBindAddress = "127.0.0.1",
            ListenerPort = 2525,
            ListenerHostName = "relaywright.test",
            MaxMessageSizeBytes = 1024 * 1024,
            UpstreamHost = "smtp.example.test",
            UpstreamPort = 587,
            UpstreamSecureSocketOptions = SecureSocketOptions.StartTls,
            UpstreamTimeoutSeconds = 30,
            DeliveryConcurrency = 1,
            MaxRetryCount = 3,
            InitialRetryDelaySeconds = 30,
            MaxRetryDelaySeconds = 300,
            MessageExpirationHours = 24,
            DeliveredRetentionHours = 24,
            FailedRetentionHours = 24,
            EventRetentionHours = 24
        };
    }

    private sealed class RelayConfigurationFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly TestDbContextFactory _dbContextFactory;

        private RelayConfigurationFixture(SqliteConnection connection, TestDbContextFactory dbContextFactory)
        {
            _connection = connection;
            _dbContextFactory = dbContextFactory;
            Service = new RelayConfigurationService(
                dbContextFactory,
                new TestSecretProtector(),
                new TestOperationalEventService(),
                new RuntimeConfigurationNotifier(),
                new TestQueueSignal(),
                NullLogger<RelayConfigurationService>.Instance);
        }

        public RelayConfigurationService Service { get; }

        public static async Task<RelayConfigurationFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var factory = new TestDbContextFactory(options);
            await using var dbContext = factory.CreateDbContext();
            await dbContext.Database.EnsureCreatedAsync();
            dbContext.RelayConfigurations.Add(new RelayConfiguration());
            await dbContext.SaveChangesAsync();

            return new RelayConfigurationFixture(connection, factory);
        }

        public async Task<RelayConfiguration> GetConfigurationAsync()
        {
            await using var dbContext = _dbContextFactory.CreateDbContext();
            return await dbContext.RelayConfigurations.AsNoTracking().SingleAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext()
        {
            return new ApplicationDbContext(options);
        }
    }

    private sealed class TestSecretProtector : ISecretProtector
    {
        public string Protect(string? plainText)
        {
            return plainText ?? string.Empty;
        }

        public string? Unprotect(string? protectedText)
        {
            return protectedText;
        }
    }

    private sealed class TestOperationalEventService : IOperationalEventService
    {
        public Task WriteAsync(OperationalEventRequest request, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestQueueSignal : IQueueSignal
    {
        public void Pulse()
        {
        }

        public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
