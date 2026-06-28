namespace Relaywright.Web.Data.Entities;

public sealed class SubmissionPolicy
{
    public int Id { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public string? AllowedSenderAddresses { get; set; }

    public string? BlockedSenderAddresses { get; set; }

    public string? AllowedRecipientDomains { get; set; }

    public string? BlockedRecipientDomains { get; set; }

    public long? MaxMessageSizeBytes { get; set; }

    public int? MaxRecipientsPerMessage { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
