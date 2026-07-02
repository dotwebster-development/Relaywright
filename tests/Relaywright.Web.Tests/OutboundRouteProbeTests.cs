using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Services.Runtime;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class OutboundRouteProbeTests
{
    [Fact]
    public async Task ProbeReportsUnavailableWhenUpstreamHostIsMissing()
    {
        var probe = new OutboundRouteProbe(NullLogger<OutboundRouteProbe>.Instance);

        var result = await probe.ProbeAsync(null, 587, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not configured", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
