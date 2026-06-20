using MailKit.Net.Smtp;
using Relaywright.Web.Configuration;

namespace Relaywright.Web.Services.Delivery;

public interface IUpstreamAuthenticationService
{
    Task AuthenticateAsync(SmtpClient client, RelayConfigurationSnapshot configuration, CancellationToken cancellationToken);
}
