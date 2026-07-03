using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Updates;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class UpdateCheckServiceTests
{
    [Theory]
    [InlineData("v1.0.1", "1.0.1")]
    [InlineData("1.0.1+build.5", "1.0.1")]
    [InlineData("1.0.1-rc.1+build.5", "1.0.1-rc.1")]
    [InlineData("1.0.0.0", "1.0.0")]
    public void SemanticVersionNormalizesSupportedForms(string value, string expected)
    {
        Assert.True(SemanticVersionInfo.TryParse(value, out var version));

        Assert.Equal(expected, version!.ToString());
    }

    [Fact]
    public void SemanticVersionOrdersPrereleaseBeforeStable()
    {
        Assert.True(SemanticVersionInfo.TryParse("1.0.1-rc.1", out var prerelease));
        Assert.True(SemanticVersionInfo.TryParse("1.0.1", out var stable));

        Assert.True(prerelease!.CompareTo(stable) < 0);
        Assert.True(stable!.CompareTo(prerelease) > 0);
    }

    [Fact]
    public void SemanticVersionRejectsInvalidTags()
    {
        Assert.False(SemanticVersionInfo.TryParse("release-one", out _));
        Assert.False(SemanticVersionInfo.TryParse("1.0.0.7", out _));
        Assert.False(SemanticVersionInfo.TryParse("1.0.0-", out _));
    }

    [Fact]
    public async Task RefreshReturnsUpdateAvailableForNewerRelease()
    {
        var service = CreateService(SuccessRelease("v9999.0.0"));

        var status = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckState.UpdateAvailable, status.State);
        Assert.Equal("9999.0.0", status.LatestVersion);
        Assert.Equal("https://example.test/releases/v9999.0.0", status.ReleaseUrl);
    }

    [Fact]
    public async Task RefreshReturnsUpToDateForMatchingRelease()
    {
        var service = CreateService(SuccessRelease($"v{ApplicationVersion.DisplayVersion}"));

        var status = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckState.UpToDate, status.State);
        Assert.Equal(ApplicationVersion.DisplayVersion, status.LatestVersion);
    }

    [Fact]
    public async Task RefreshReturnsCurrentAheadForOlderLatestRelease()
    {
        var service = CreateService(SuccessRelease("v0.0.1"));

        var status = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckState.CurrentAhead, status.State);
        Assert.Equal("0.0.1", status.LatestVersion);
    }

    [Fact]
    public async Task RefreshReturnsCheckFailedForHttpFailure()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.Forbidden));

        var status = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckState.CheckFailed, status.State);
        Assert.Contains("HTTP 403", status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshReturnsCheckFailedForMalformedJson()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not-json")
        });

        var status = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckState.CheckFailed, status.State);
    }

    [Fact]
    public async Task RefreshReturnsInvalidReleaseForInvalidTag()
    {
        var service = CreateService(SuccessRelease("release-one"));

        var status = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckState.InvalidRelease, status.State);
    }

    [Fact]
    public async Task DisabledChecksDoNotCallGitHub()
    {
        var handler = new QueueHandler();
        var service = CreateService(handler, new UpdateCheckOptions { Enabled = false });

        var status = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckState.Disabled, status.State);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ForcedRefreshCallsGitHubEachTime()
    {
        var handler = new QueueHandler(
            () => SuccessRelease($"v{ApplicationVersion.DisplayVersion}"),
            () => SuccessRelease("v9999.0.0"));
        var service = CreateService(handler);

        var first = await service.RefreshAsync(CancellationToken.None);
        var second = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckState.UpToDate, first.State);
        Assert.Equal(UpdateCheckState.UpdateAvailable, second.State);
        Assert.Equal(2, handler.CallCount);
    }

    private static HttpResponseMessage SuccessRelease(string tagName)
    {
        var json = $$"""
            {
              "tag_name": "{{tagName}}",
              "name": "Relaywright {{tagName}}",
              "html_url": "https://example.test/releases/{{tagName}}",
              "draft": false,
              "prerelease": false,
              "published_at": "2030-01-01T00:00:00Z"
            }
            """;

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
    }

    private static UpdateCheckService CreateService(HttpResponseMessage response)
    {
        return CreateService(new QueueHandler(() => response));
    }

    private static UpdateCheckService CreateService(QueueHandler handler, UpdateCheckOptions? options = null)
    {
        return new UpdateCheckService(
            Microsoft.Extensions.Options.Options.Create(options ?? new UpdateCheckOptions()),
            new StaticHttpClientFactory(new HttpClient(handler)),
            NullLogger<UpdateCheckService>.Instance);
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class QueueHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new(responses);

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount += 1;
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No fake HTTP response was configured.");
            }

            return Task.FromResult(_responses.Dequeue()());
        }
    }
}
