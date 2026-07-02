using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Data;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class ApplicationRestartServiceTests
{
    [Fact]
    public async Task DevelopmentRestartRequestPersistsFallbackBannerWithoutStoppingApplication()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        var lifetime = new TestHostApplicationLifetime();
        var service = new ApplicationRestartService(
            database.DbContextFactory,
            lifetime,
            new TestHostEnvironment(Environments.Development),
            new RecordingOperationalEventService(),
            NullLogger<ApplicationRestartService>.Instance);

        var result = await service.RequestRestartAsync("Certificate changed.", "admin", CancellationToken.None);

        await using var dbContext = database.CreateDbContext();
        var state = await dbContext.RuntimeControlStates.SingleAsync();
        Assert.False(result.RestartScheduled);
        Assert.True(state.RestartRequired);
        Assert.False(state.RestartSupported);
        Assert.Equal("Certificate changed.", state.RestartReason);
        Assert.False(lifetime.StopRequested);
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public bool StopRequested { get; private set; }

        public CancellationToken ApplicationStarted => _started.Token;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication()
        {
            StopRequested = true;
            _stopping.Cancel();
        }
    }

    private sealed class TestHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Relaywright.Web.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
