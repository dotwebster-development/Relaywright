using System.Net;
using System.Net.Sockets;

namespace Relaywright.Web.Services.Runtime;

public sealed class OutboundRouteProbe(ILogger<OutboundRouteProbe> logger) : IOutboundRouteProbe
{
    public async Task<OutboundRouteResult> ProbeAsync(
        string? host,
        int port,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return new OutboundRouteResult
            {
                Succeeded = false,
                Message = "Upstream host is not configured."
            };
        }

        if (port is < 1 or > 65535)
        {
            return new OutboundRouteResult
            {
                Succeeded = false,
                Message = "Upstream port is invalid."
            };
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host.Trim(), cancellationToken);
            foreach (var address in addresses.Where(x => x.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
            {
                using var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect(new IPEndPoint(address, port));
                if (socket.LocalEndPoint is IPEndPoint localEndPoint)
                {
                    return new OutboundRouteResult
                    {
                        Succeeded = true,
                        LocalIpAddress = localEndPoint.Address.ToString(),
                        RemoteAddress = $"{address}:{port}",
                        Message = $"Local source IP is {localEndPoint.Address}."
                    };
                }
            }

            return new OutboundRouteResult
            {
                Succeeded = false,
                Message = "No IPv4 or IPv6 address could be resolved for the upstream host."
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Outgoing route probe failed. Host={Host}; Port={Port}",
                host,
                port);

            return new OutboundRouteResult
            {
                Succeeded = false,
                Message = exception.Message
            };
        }
    }
}
