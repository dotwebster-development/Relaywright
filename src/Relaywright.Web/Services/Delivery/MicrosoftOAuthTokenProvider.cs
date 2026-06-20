using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Delivery;

public sealed class MicrosoftOAuthTokenProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<MicrosoftOAuthTokenProvider> logger)
{
    private const string OutlookScope = "https://outlook.office365.com/.default";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cachedKey;
    private string? _cachedToken;
    private DateTimeOffset _cachedExpiresUtc = DateTimeOffset.MinValue;

    public async Task<string> GetAccessTokenAsync(RelayConfigurationSnapshot configuration, CancellationToken cancellationToken)
    {
        if (configuration.UpstreamAuthenticationMode != UpstreamAuthenticationMode.Microsoft365OAuth)
        {
            throw new InvalidOperationException("Microsoft OAuth token acquisition was requested for a non-Microsoft auth mode.");
        }

        if (string.IsNullOrWhiteSpace(configuration.MicrosoftTenantId)
            || string.IsNullOrWhiteSpace(configuration.MicrosoftClientId)
            || string.IsNullOrWhiteSpace(configuration.MicrosoftClientSecret))
        {
            throw new InvalidOperationException("Microsoft 365 OAuth requires tenant ID, client ID, and client secret.");
        }

        var cacheKey = string.Join(
            "|",
            configuration.MicrosoftTenantId.Trim(),
            configuration.MicrosoftClientId.Trim(),
            HashSecret(configuration.MicrosoftClientSecret),
            configuration.UpstreamUserName?.Trim() ?? string.Empty);

        var now = DateTimeOffset.UtcNow;
        if (_cachedKey == cacheKey && !string.IsNullOrWhiteSpace(_cachedToken) && _cachedExpiresUtc > now)
        {
            logger.LogDebug(
                "Using cached Microsoft OAuth token. TenantId={TenantId}; ClientId={ClientId}; Mailbox={Mailbox}; ExpiresUtc={ExpiresUtc}",
                configuration.MicrosoftTenantId,
                configuration.MicrosoftClientId,
                configuration.UpstreamUserName,
                _cachedExpiresUtc);
            return _cachedToken;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (_cachedKey == cacheKey && !string.IsNullOrWhiteSpace(_cachedToken) && _cachedExpiresUtc > now)
            {
                logger.LogDebug(
                    "Using cached Microsoft OAuth token after lock wait. TenantId={TenantId}; ClientId={ClientId}; Mailbox={Mailbox}; ExpiresUtc={ExpiresUtc}",
                    configuration.MicrosoftTenantId,
                    configuration.MicrosoftClientId,
                    configuration.UpstreamUserName,
                    _cachedExpiresUtc);
                return _cachedToken;
            }

            logger.LogInformation(
                "Requesting Microsoft OAuth token. TenantId={TenantId}; ClientId={ClientId}; Mailbox={Mailbox}",
                configuration.MicrosoftTenantId,
                configuration.MicrosoftClientId,
                configuration.UpstreamUserName);

            using var client = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://login.microsoftonline.com/{Uri.EscapeDataString(configuration.MicrosoftTenantId.Trim())}/oauth2/v2.0/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = configuration.MicrosoftClientId.Trim(),
                    ["client_secret"] = configuration.MicrosoftClientSecret,
                    ["scope"] = OutlookScope,
                    ["grant_type"] = "client_credentials"
                })
            };

            using var response = await client.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var message = $"Microsoft token request failed with {(int)response.StatusCode} {response.ReasonPhrase}.";
                logger.LogWarning(
                    "Microsoft OAuth token request failed. StatusCode={StatusCode}; ReasonPhrase={ReasonPhrase}; TenantId={TenantId}; ClientId={ClientId}; Mailbox={Mailbox}",
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    configuration.MicrosoftTenantId,
                    configuration.MicrosoftClientId,
                    configuration.UpstreamUserName);

                if ((int)response.StatusCode >= 500)
                {
                    throw new HttpRequestException(message);
                }

                throw new InvalidOperationException($"{message} {payload}");
            }

            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("access_token", out var accessTokenElement))
            {
                throw new InvalidOperationException("Microsoft token response did not include an access token.");
            }

            var accessToken = accessTokenElement.GetString();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("Microsoft token response returned an empty access token.");
            }

            var expiresInSeconds = document.RootElement.TryGetProperty("expires_in", out var expiresInElement)
                && expiresInElement.TryGetInt32(out var parsedExpiresIn)
                ? parsedExpiresIn
                : 3600;

            _cachedKey = cacheKey;
            _cachedToken = accessToken;
            _cachedExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresInSeconds - 300));

            logger.LogInformation(
                "Microsoft OAuth token acquired. TenantId={TenantId}; ClientId={ClientId}; Mailbox={Mailbox}; ExpiresUtc={ExpiresUtc}; ExpiresInSeconds={ExpiresInSeconds}",
                configuration.MicrosoftTenantId,
                configuration.MicrosoftClientId,
                configuration.UpstreamUserName,
                _cachedExpiresUtc,
                expiresInSeconds);

            return accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string HashSecret(string secret)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }
}
