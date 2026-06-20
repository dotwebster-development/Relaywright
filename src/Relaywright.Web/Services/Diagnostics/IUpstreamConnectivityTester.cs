using Relaywright.Web.Configuration;

namespace Relaywright.Web.Services.Diagnostics;

public interface IUpstreamConnectivityTester
{
    Task<ConnectivityTestResult> TestAsync(RelayConfigurationSnapshot configuration, CancellationToken cancellationToken);
}

