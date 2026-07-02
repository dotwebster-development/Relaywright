namespace Relaywright.Web.Services.Runtime;

public interface IOutboundRouteProbe
{
    Task<OutboundRouteResult> ProbeAsync(
        string? host,
        int port,
        CancellationToken cancellationToken);
}
