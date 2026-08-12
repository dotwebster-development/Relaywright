using Relaywright.Web.Data.Entities;
using Relaywright.Web.Validation;

namespace Relaywright.Web.Services.Security;

public sealed class SubmissionPolicyValidator
{
    public void Validate(SubmissionPolicy policy)
    {
        if (policy.Id != 1)
        {
            throw new InvalidOperationException("Submission policy ID must be 1.");
        }

        ValidatePolicyValues(
            policy.AllowedSenderAddresses,
            policy.BlockedSenderAddresses,
            policy.AllowedRecipientDomains,
            policy.BlockedRecipientDomains,
            policy.MaxMessageSizeBytes,
            policy.MaxRecipientsPerMessage);
    }

    public void ValidateProfile(TrustedNetwork profile)
    {
        ValidatePolicyValues(
            profile.AllowedSenderAddresses,
            profile.BlockedSenderAddresses,
            profile.AllowedRecipientDomains,
            profile.BlockedRecipientDomains,
            profile.MaxMessageSizeBytes,
            profile.MaxRecipientsPerMessage);
    }

    public string? NormalizeList(string? value)
    {
        return ValidationRules.NormalizeDelimitedList(value);
    }

    public long? NormalizePositive(long? value) => value is > 0 ? value : null;

    public int? NormalizePositive(int? value) => value is > 0 ? value : null;

    private static void ValidatePolicyValues(
        string? allowedSenderAddresses,
        string? blockedSenderAddresses,
        string? allowedRecipientDomains,
        string? blockedRecipientDomains,
        long? maxMessageSizeBytes,
        int? maxRecipientsPerMessage)
    {
        ValidateSenderPolicyList(allowedSenderAddresses, "Allowed sender addresses");
        ValidateSenderPolicyList(blockedSenderAddresses, "Blocked sender addresses");
        ValidateRecipientDomainPolicyList(allowedRecipientDomains, "Allowed recipient domains");
        ValidateRecipientDomainPolicyList(blockedRecipientDomains, "Blocked recipient domains");
        ServiceValidation.RequirePositive(maxMessageSizeBytes, "Maximum message size");
        ServiceValidation.RequirePositive(maxRecipientsPerMessage, "Maximum recipients per message");
    }

    private static void ValidateSenderPolicyList(string? value, string label)
    {
        ServiceValidation.RequirePolicyListLength(value, label);

        foreach (var entry in ValidationRules.SplitDelimitedList(value))
        {
            if (!ValidationRules.IsSenderPolicyPattern(entry))
            {
                throw new InvalidOperationException($"{label} contains an invalid sender entry: {entry}.");
            }
        }
    }

    private static void ValidateRecipientDomainPolicyList(string? value, string label)
    {
        ServiceValidation.RequirePolicyListLength(value, label);

        foreach (var entry in ValidationRules.SplitDelimitedList(value))
        {
            if (!ValidationRules.IsRecipientDomainPattern(entry))
            {
                throw new InvalidOperationException($"{label} contains an invalid domain entry: {entry}.");
            }
        }
    }
}
