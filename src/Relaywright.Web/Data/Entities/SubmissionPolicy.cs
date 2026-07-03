using System.ComponentModel.DataAnnotations;
using Relaywright.Web.Validation;

namespace Relaywright.Web.Data.Entities;

public sealed class SubmissionPolicy
{
    [Range(1, 1)]
    public int Id { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    [StringLength(ValidationLimits.MaximumPolicyListLength)]
    [NoControlCharacters(true)]
    [SenderPolicyList]
    public string? AllowedSenderAddresses { get; set; }

    [StringLength(ValidationLimits.MaximumPolicyListLength)]
    [NoControlCharacters(true)]
    [SenderPolicyList]
    public string? BlockedSenderAddresses { get; set; }

    [StringLength(ValidationLimits.MaximumPolicyListLength)]
    [NoControlCharacters(true)]
    [RecipientDomainPolicyList]
    public string? AllowedRecipientDomains { get; set; }

    [StringLength(ValidationLimits.MaximumPolicyListLength)]
    [NoControlCharacters(true)]
    [RecipientDomainPolicyList]
    public string? BlockedRecipientDomains { get; set; }

    [Range(typeof(long), "1", "9223372036854775807")]
    public long? MaxMessageSizeBytes { get; set; }

    [Range(1, int.MaxValue)]
    public int? MaxRecipientsPerMessage { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
