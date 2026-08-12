namespace Relaywright.Web.Options;

public sealed class QueueProcessingOptions
{
    public const string SectionName = "QueueProcessing";
    public const int DefaultStaleClaimMinutes = 15;
    public const int MinimumStaleClaimMinutes = 1;
    public const int MaximumStaleClaimMinutes = 24 * 60;

    public int StaleClaimMinutes { get; set; } = DefaultStaleClaimMinutes;

    public TimeSpan GetStaleClaimThreshold()
    {
        return TimeSpan.FromMinutes(StaleClaimMinutes);
    }
}
