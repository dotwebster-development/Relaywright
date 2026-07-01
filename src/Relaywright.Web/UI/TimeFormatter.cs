namespace Relaywright.Web.UI;

public sealed record HeartbeatFreshness(string Text, string BadgeClass);

public static class TimeFormatter
{
    private static readonly TimeSpan FreshHeartbeat = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CriticalHeartbeat = TimeSpan.FromMinutes(10);

    public static string FormatRelative(DateTimeOffset? value, DateTimeOffset now)
    {
        if (value is null)
        {
            return "Never";
        }

        var delta = value.Value - now;
        var duration = FormatDuration(delta.Duration());
        if (duration == "now")
        {
            return duration;
        }

        return delta >= TimeSpan.Zero ? $"in {duration}" : $"{duration} ago";
    }

    public static string FormatAge(DateTimeOffset value, DateTimeOffset now)
    {
        var age = now - value;
        return age <= TimeSpan.Zero ? "now" : $"{FormatDuration(age)} old";
    }

    public static HeartbeatFreshness FormatHeartbeat(DateTimeOffset? heartbeatUtc, DateTimeOffset now)
    {
        if (heartbeatUtc is null)
        {
            return new HeartbeatFreshness("Never reported", "status-unknown");
        }

        var age = now - heartbeatUtc.Value;
        if (age <= FreshHeartbeat)
        {
            return new HeartbeatFreshness($"Fresh ({FormatRelative(heartbeatUtc, now)})", "status-enabled");
        }

        if (age <= CriticalHeartbeat)
        {
            return new HeartbeatFreshness($"Stale ({FormatRelative(heartbeatUtc, now)})", "status-inprogress");
        }

        return new HeartbeatFreshness($"Critical ({FormatRelative(heartbeatUtc, now)})", "status-failed");
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 45)
        {
            return "now";
        }

        if (duration.TotalMinutes < 60)
        {
            return $"{Math.Max(1, (int)Math.Round(duration.TotalMinutes))}m";
        }

        if (duration.TotalHours < 48)
        {
            return $"{Math.Max(1, (int)Math.Round(duration.TotalHours))}h";
        }

        return $"{Math.Max(1, (int)Math.Round(duration.TotalDays))}d";
    }
}
