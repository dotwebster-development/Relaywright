namespace Relaywright.Web.Data.Entities;

public sealed class TrustedNetwork
{
    public int Id { get; set; }

    public string Cidr { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? Owner { get; set; }

    public string? Location { get; set; }

    public string? AllowedSenderAddresses { get; set; }

    public string? BlockedSenderAddresses { get; set; }

    public string? AllowedRecipientDomains { get; set; }

    public string? BlockedRecipientDomains { get; set; }

    public long? MaxMessageSizeBytes { get; set; }

    public int? MaxRecipientsPerMessage { get; set; }

    public int? RateLimitMessagesPerHour { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
