namespace Relaywright.Web.Services.Security;

public sealed class TrustedDevicePolicySummary
{
    public int TrustedNetworkId { get; init; }

    public bool GlobalPolicyEnabled { get; init; }

    public EffectivePolicyLimit MessageSizeLimit { get; init; } = new();

    public EffectivePolicyLimit RecipientLimit { get; init; } = new();

    public int? RateLimitMessagesPerHour { get; init; }

    public int AllowedSenderRuleCount { get; init; }

    public int BlockedSenderRuleCount { get; init; }

    public int AllowedRecipientDomainRuleCount { get; init; }

    public int BlockedRecipientDomainRuleCount { get; init; }
}

public sealed class EffectivePolicyLimit
{
    public long? Value { get; init; }

    public string Source { get; init; } = "Not set";
}
