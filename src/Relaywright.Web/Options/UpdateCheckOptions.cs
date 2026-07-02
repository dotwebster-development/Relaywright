namespace Relaywright.Web.Options;

public sealed class UpdateCheckOptions
{
    public const string SectionName = "UpdateCheck";
    public const string DefaultRepository = "dotwebster-development/Relaywright";

    public bool Enabled { get; set; } = true;

    public string Repository { get; set; } = DefaultRepository;

    public int IntervalHours { get; set; } = 24;

    public int TimeoutSeconds { get; set; } = 10;

    public int StartupDelaySeconds { get; set; } = 30;

    public TimeSpan GetInterval()
    {
        return TimeSpan.FromHours(Math.Clamp(IntervalHours, 1, 24 * 14));
    }

    public TimeSpan GetTimeout()
    {
        return TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 60));
    }

    public TimeSpan GetStartupDelay()
    {
        return TimeSpan.FromSeconds(Math.Clamp(StartupDelaySeconds, 0, 300));
    }
}
