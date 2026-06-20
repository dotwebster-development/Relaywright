using Relaywright.Web.Configuration;

namespace Relaywright.Web.Services.Diagnostics;

public interface IUpstreamTestEmailSender
{
    Task<TestEmailResult> SendAsync(
        TestEmailRequest request,
        RelayConfigurationSnapshot configuration,
        Guid sessionId,
        CancellationToken cancellationToken);
}
