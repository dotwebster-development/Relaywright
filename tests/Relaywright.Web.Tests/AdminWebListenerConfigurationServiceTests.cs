using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Security;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class AdminWebListenerConfigurationServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task SavePersistsHttpsAndHttpUrls()
    {
        using var fixture = ListenerFixture.Create();

        var saved = await fixture.Service.SaveAsync(new AdminWebListenerConfiguration
        {
            HttpsPort = 9443,
            EnableHttp = true,
            HttpPort = 9080
        }, CancellationToken.None);

        Assert.Equal(["https://*:9443", "http://*:9080"], saved.GetUrls());
        Assert.True(File.Exists(fixture.Paths.AdminWebListenerConfigurationPath));

        var loaded = AdminWebListenerConfigurationService.LoadConfiguration(fixture.Paths);

        Assert.NotNull(loaded);
        Assert.Equal(["https://*:9443", "http://*:9080"], loaded.GetUrls());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SaveAllowsHttpDisabled()
    {
        using var fixture = ListenerFixture.Create();

        var saved = await fixture.Service.SaveAsync(new AdminWebListenerConfiguration
        {
            HttpsPort = 9443,
            EnableHttp = false,
            HttpPort = 9080
        }, CancellationToken.None);

        Assert.Equal(["https://*:9443"], saved.GetUrls());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SaveRejectsDuplicatePortsWhenHttpEnabled()
    {
        using var fixture = ListenerFixture.Create();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.SaveAsync(new AdminWebListenerConfiguration
            {
                HttpsPort = 9443,
                EnableHttp = true,
                HttpPort = 9443
            }, CancellationToken.None));

        Assert.Contains("different", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ListenerFixture : IDisposable
    {
        private ListenerFixture(string root, AppPaths paths, AdminWebListenerConfigurationService service)
        {
            Root = root;
            Paths = paths;
            Service = service;
        }

        private string Root { get; }

        public AppPaths Paths { get; }

        public AdminWebListenerConfigurationService Service { get; }

        public static ListenerFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"relaywright-web-listener-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            var paths = new AppPaths(root, new StorageOptions());
            paths.EnsureCreated();

            var service = new AdminWebListenerConfigurationService(
                paths,
                NullLogger<AdminWebListenerConfigurationService>.Instance);

            return new ListenerFixture(root, paths, service);
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
