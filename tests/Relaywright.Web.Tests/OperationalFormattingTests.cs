using Relaywright.Web.UI;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class OperationalFormattingTests
{
    [Fact]
    public void HeartbeatFreshnessUsesPlannedThresholds()
    {
        var now = DateTimeOffset.UtcNow;

        var fresh = TimeFormatter.FormatHeartbeat(now.AddSeconds(-30), now);
        var stale = TimeFormatter.FormatHeartbeat(now.AddMinutes(-5), now);
        var critical = TimeFormatter.FormatHeartbeat(now.AddMinutes(-11), now);
        var missing = TimeFormatter.FormatHeartbeat(null, now);

        Assert.Equal("status-enabled", fresh.BadgeClass);
        Assert.Equal("status-inprogress", stale.BadgeClass);
        Assert.Equal("status-failed", critical.BadgeClass);
        Assert.Equal("status-unknown", missing.BadgeClass);
    }
}
