using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Delivery;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class MicrosoftOAuthTokenProviderTests
{
    [Fact]
    public async Task TokenFailureDoesNotIncludeResponsePayloadInException()
    {
        using var client = new HttpClient(new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            ReasonPhrase = "Bad Request",
            Content = new StringContent(
                "{\"error\":\"invalid_client\",\"error_description\":\"contains super-secret-value\",\"access_token\":\"token-value\"}")
        }));
        var provider = new MicrosoftOAuthTokenProvider(
            new StaticHttpClientFactory(client),
            NullLogger<MicrosoftOAuthTokenProvider>.Instance);
        var configuration = new RelayConfigurationSnapshot
        {
            UpstreamAuthenticationMode = UpstreamAuthenticationMode.Microsoft365OAuth,
            MicrosoftTenantId = "tenant-id",
            MicrosoftClientId = "client-id",
            MicrosoftClientSecret = "super-secret-value",
            UpstreamUserName = "mailbox@example.test"
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetAccessTokenAsync(configuration, CancellationToken.None));

        Assert.Contains("Microsoft token request failed with 400 Bad Request.", exception.Message);
        Assert.DoesNotContain("invalid_client", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("super-secret-value", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token-value", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }
}
