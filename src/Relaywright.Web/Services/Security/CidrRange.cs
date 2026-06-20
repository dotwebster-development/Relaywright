using System.Net;
using System.Net.Sockets;

namespace Relaywright.Web.Services.Security;

public sealed class CidrRange
{
    private CidrRange(IPAddress network, int prefixLength)
    {
        Network = network;
        PrefixLength = prefixLength;
    }

    public IPAddress Network { get; }

    public int PrefixLength { get; }

    public static bool TryParse(string value, out CidrRange? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!IPAddress.TryParse(parts[0], out var network))
        {
            return false;
        }

        int prefixLength;
        if (parts.Length == 1)
        {
            prefixLength = network.AddressFamily switch
            {
                AddressFamily.InterNetwork => 32,
                AddressFamily.InterNetworkV6 => 128,
                _ => -1
            };
        }
        else if (!int.TryParse(parts[1], out prefixLength))
        {
            return false;
        }

        var maxPrefix = network.AddressFamily switch
        {
            AddressFamily.InterNetwork => 32,
            AddressFamily.InterNetworkV6 => 128,
            _ => -1
        };

        if (prefixLength < 0 || prefixLength > maxPrefix)
        {
            return false;
        }

        result = new CidrRange(network, prefixLength);
        return true;
    }

    public bool Contains(IPAddress address)
    {
        if (address.AddressFamily != Network.AddressFamily)
        {
            return false;
        }

        var networkBytes = Network.GetAddressBytes();
        var addressBytes = address.GetAddressBytes();
        var wholeBytes = PrefixLength / 8;
        var remainingBits = PrefixLength % 8;

        for (var i = 0; i < wholeBytes; i++)
        {
            if (networkBytes[i] != addressBytes[i])
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xff << (8 - remainingBits));
        return (networkBytes[wholeBytes] & mask) == (addressBytes[wholeBytes] & mask);
    }
}

