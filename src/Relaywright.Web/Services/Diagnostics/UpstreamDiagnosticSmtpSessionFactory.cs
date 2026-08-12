using MailKit.Net.Smtp;
using Relaywright.Web.Services.Delivery;

namespace Relaywright.Web.Services.Diagnostics;

public sealed class UpstreamDiagnosticSmtpSessionFactory(
    IUpstreamAuthenticationService authenticationService) : IUpstreamDiagnosticSmtpSessionFactory
{
    public IUpstreamDiagnosticSmtpSession Create(int timeoutMilliseconds)
    {
        var client = new SmtpClient
        {
            Timeout = timeoutMilliseconds
        };
        return new UpstreamDiagnosticSmtpSession(client, authenticationService);
    }
}
