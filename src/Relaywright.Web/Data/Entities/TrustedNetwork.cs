using System.ComponentModel.DataAnnotations;
using Relaywright.Web.Validation;

namespace Relaywright.Web.Data.Entities;

public sealed class TrustedNetwork
{
    [Range(0, int.MaxValue)]
    public int Id { get; set; }

    [RequiredNonWhiteSpace]
    [StringLength(128)]
    [CidrRange]
    public string Cidr { get; set; } = string.Empty;

    [RequiredNonWhiteSpace]
    [StringLength(256)]
    [NoControlCharacters]
    public string Description { get; set; } = string.Empty;

    [StringLength(256)]
    [NoControlCharacters]
    public string? Owner { get; set; }

    [StringLength(256)]
    [NoControlCharacters]
    public string? Location { get; set; }

    [StringLength(4096)]
    [NoControlCharacters(true)]
    [SenderPolicyList]
    public string? AllowedSenderAddresses { get; set; }

    [StringLength(4096)]
    [NoControlCharacters(true)]
    [SenderPolicyList]
    public string? BlockedSenderAddresses { get; set; }

    [StringLength(4096)]
    [NoControlCharacters(true)]
    [RecipientDomainPolicyList]
    public string? AllowedRecipientDomains { get; set; }

    [StringLength(4096)]
    [NoControlCharacters(true)]
    [RecipientDomainPolicyList]
    public string? BlockedRecipientDomains { get; set; }

    [Range(typeof(long), "1", "9223372036854775807")]
    public long? MaxMessageSizeBytes { get; set; }

    [Range(1, int.MaxValue)]
    public int? MaxRecipientsPerMessage { get; set; }

    [Range(1, int.MaxValue)]
    public int? RateLimitMessagesPerHour { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
