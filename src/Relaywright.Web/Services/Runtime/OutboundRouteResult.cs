namespace Relaywright.Web.Services.Runtime;

public sealed class OutboundRouteResult
{
    public bool Succeeded { get; init; }

    public string? LocalIpAddress { get; init; }

    public string? RemoteAddress { get; init; }

    public string Message { get; init; } = string.Empty;
}
