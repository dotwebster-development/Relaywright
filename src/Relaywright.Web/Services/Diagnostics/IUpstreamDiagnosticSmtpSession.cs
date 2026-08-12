using MimeKit;
using Relaywright.Web.Configuration;

namespace Relaywright.Web.Services.Diagnostics;

public interface IUpstreamDiagnosticSmtpSession : IDisposable
{
    bool IsSecure { get; }

    bool IsConnected { get; }

    Task ConnectAsync(RelayConfigurationSnapshot configuration, CancellationToken cancellationToken);

    Task AuthenticateAsync(RelayConfigurationSnapshot configuration, CancellationToken cancellationToken);

    Task<string> SendAsync(MimeMessage message, CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}
