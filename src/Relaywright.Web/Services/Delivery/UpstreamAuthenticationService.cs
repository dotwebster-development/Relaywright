using MailKit.Net.Smtp;
using MailKit.Security;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Delivery;

public sealed class UpstreamAuthenticationService(
    MicrosoftOAuthTokenProvider microsoftOAuthTokenProvider,
    ILogger<UpstreamAuthenticationService> logger) : IUpstreamAuthenticationService
{
    public async Task AuthenticateAsync(SmtpClient client, RelayConfigurationSnapshot configuration, CancellationToken cancellationToken)
    {
        if (!configuration.UseUpstreamAuthentication)
        {
            logger.LogInformation("Skipping upstream authentication because authentication is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(configuration.UpstreamUserName))
        {
            logger.LogError("Upstream authentication cannot start because the user name/mailbox is missing.");
            throw new InvalidOperationException("Upstream authentication requires a user name or mailbox.");
        }

        switch (configuration.UpstreamAuthenticationMode)
        {
            case UpstreamAuthenticationMode.Basic:
                logger.LogInformation("Starting upstream basic authentication. UserName={UserName}", configuration.UpstreamUserName);
                await client.AuthenticateAsync(
                    configuration.UpstreamUserName,
                    configuration.UpstreamPassword ?? string.Empty,
                    cancellationToken);
                logger.LogInformation("Upstream basic authentication succeeded. UserName={UserName}", configuration.UpstreamUserName);
                break;

            case UpstreamAuthenticationMode.Microsoft365OAuth:
                logger.LogInformation("Starting upstream Microsoft 365 OAuth authentication. Mailbox={Mailbox}; TenantConfigured={TenantConfigured}; ClientConfigured={ClientConfigured}",
                    configuration.UpstreamUserName,
                    !string.IsNullOrWhiteSpace(configuration.MicrosoftTenantId),
                    !string.IsNullOrWhiteSpace(configuration.MicrosoftClientId));
                var accessToken = await microsoftOAuthTokenProvider.GetAccessTokenAsync(configuration, cancellationToken);
                var oauth2 = new SaslMechanismOAuth2(configuration.UpstreamUserName, accessToken);
                await client.AuthenticateAsync(oauth2, cancellationToken);
                logger.LogInformation("Upstream Microsoft 365 OAuth authentication succeeded. Mailbox={Mailbox}", configuration.UpstreamUserName);
                break;

            default:
                logger.LogError("Unsupported upstream authentication mode configured. Mode={Mode}", configuration.UpstreamAuthenticationMode);
                throw new InvalidOperationException("The configured upstream authentication mode is not supported.");
        }
    }
}
