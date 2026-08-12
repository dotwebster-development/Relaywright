using MailKit.Net.Smtp;
using MimeKit;
using Relaywright.Web.Configuration;
using Relaywright.Web.Services.Delivery;

namespace Relaywright.Web.Services.Diagnostics;

public sealed class UpstreamDiagnosticSmtpSession(
    SmtpClient client,
    IUpstreamAuthenticationService authenticationService) : IUpstreamDiagnosticSmtpSession
{
    public bool IsSecure => client.IsSecure;

    public bool IsConnected => client.IsConnected;

    public Task ConnectAsync(RelayConfigurationSnapshot configuration, CancellationToken cancellationToken)
    {
        return client.ConnectAsync(
            configuration.UpstreamHost,
            configuration.UpstreamPort,
            configuration.UpstreamSecureSocketOptions,
            cancellationToken);
    }

    public Task AuthenticateAsync(RelayConfigurationSnapshot configuration, CancellationToken cancellationToken)
    {
        return authenticationService.AuthenticateAsync(client, configuration, cancellationToken);
    }

    public Task<string> SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        return client.SendAsync(message, cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        return client.DisconnectAsync(true, cancellationToken);
    }

    public void Dispose()
    {
        client.Dispose();
    }
}
