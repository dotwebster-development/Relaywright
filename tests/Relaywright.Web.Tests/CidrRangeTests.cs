using System.Net;
using Relaywright.Web.Services.Security;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class CidrRangeTests
{
    [Fact]
    public void ParsesIpv4AddressAsSingleHost()
    {
        var parsed = CidrRange.TryParse("192.168.1.10", out var range);

        Assert.True(parsed);
        Assert.NotNull(range);
        Assert.True(range!.Contains(IPAddress.Parse("192.168.1.10")));
        Assert.False(range.Contains(IPAddress.Parse("192.168.1.11")));
    }

    [Fact]
    public void MatchesIpv4Subnet()
    {
        var parsed = CidrRange.TryParse("10.20.30.0/24", out var range);

        Assert.True(parsed);
        Assert.NotNull(range);
        Assert.True(range!.Contains(IPAddress.Parse("10.20.30.55")));
        Assert.False(range.Contains(IPAddress.Parse("10.20.31.1")));
    }

    [Fact]
    public void MatchesIpv6Subnet()
    {
        var parsed = CidrRange.TryParse("2001:db8::/64", out var range);

        Assert.True(parsed);
        Assert.NotNull(range);
        Assert.True(range!.Contains(IPAddress.Parse("2001:db8::1234")));
        Assert.False(range.Contains(IPAddress.Parse("2001:db9::1")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-ip")]
    [InlineData("192.168.1.0/33")]
    [InlineData("2001:db8::/129")]
    public void RejectsInvalidRanges(string value)
    {
        var parsed = CidrRange.TryParse(value, out var range);

        Assert.False(parsed);
        Assert.Null(range);
    }

    [Fact]
    public void Ipv4ZeroPrefixMatchesAnyIpv4AddressOnly()
    {
        var parsed = CidrRange.TryParse("0.0.0.0/0", out var range);

        Assert.True(parsed);
        Assert.NotNull(range);
        Assert.True(range!.Contains(IPAddress.Parse("203.0.113.10")));
        Assert.False(range.Contains(IPAddress.Parse("2001:db8::1")));
    }
}
