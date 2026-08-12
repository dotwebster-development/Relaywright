namespace Relaywright.Web.Services.Diagnostics;

public interface IUpstreamDiagnosticSmtpSessionFactory
{
    IUpstreamDiagnosticSmtpSession Create(int timeoutMilliseconds);
}
