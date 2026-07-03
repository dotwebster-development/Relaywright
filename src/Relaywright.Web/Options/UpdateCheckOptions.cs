namespace Relaywright.Web.Options;

public sealed class UpdateCheckOptions
{
    public const string SectionName = "UpdateCheck";
    public const string DefaultRepository = "dotwebster-development/Relaywright";
    public const int DefaultIntervalHours = 24;
    public const int MinimumIntervalHours = 1;
    public const int MaximumIntervalHours = 24 * 14;
    public const int DefaultTimeoutSeconds = 10;
    public const int MinimumTimeoutSeconds = 1;
    public const int MaximumTimeoutSeconds = 60;
    public const int DefaultStartupDelaySeconds = 30;
    public const int MinimumStartupDelaySeconds = 0;
    public const int MaximumStartupDelaySeconds = 300;

    public bool Enabled { get; set; } = true;

    public string Repository { get; set; } = DefaultRepository;

    public int IntervalHours { get; set; } = DefaultIntervalHours;

    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    public int StartupDelaySeconds { get; set; } = DefaultStartupDelaySeconds;

    public TimeSpan GetInterval()
    {
        return TimeSpan.FromHours(Math.Clamp(IntervalHours, MinimumIntervalHours, MaximumIntervalHours));
    }

    public TimeSpan GetTimeout()
    {
        return TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, MinimumTimeoutSeconds, MaximumTimeoutSeconds));
    }

    public TimeSpan GetStartupDelay()
    {
        return TimeSpan.FromSeconds(Math.Clamp(StartupDelaySeconds, MinimumStartupDelaySeconds, MaximumStartupDelaySeconds));
    }
}
