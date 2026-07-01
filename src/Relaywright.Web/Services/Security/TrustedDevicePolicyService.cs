using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.Security;

public sealed class TrustedDevicePolicyService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IOperationalEventService eventService,
    ILogger<TrustedDevicePolicyService> logger) : ITrustedDevicePolicyService
{
    public async Task<SubmissionPolicy> GetPolicyAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var policy = await dbContext.SubmissionPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        return policy ?? new SubmissionPolicy();
    }

    public async Task SavePolicyAsync(SubmissionPolicy policy, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.SubmissionPolicies.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (existing is null)
        {
            existing = new SubmissionPolicy();
            dbContext.SubmissionPolicies.Add(existing);
        }

        existing.IsEnabled = policy.IsEnabled;
        existing.AllowedSenderAddresses = NormalizeList(policy.AllowedSenderAddresses);
        existing.BlockedSenderAddresses = NormalizeList(policy.BlockedSenderAddresses);
        existing.AllowedRecipientDomains = NormalizeList(policy.AllowedRecipientDomains);
        existing.BlockedRecipientDomains = NormalizeList(policy.BlockedRecipientDomains);
        existing.MaxMessageSizeBytes = NormalizePositive(policy.MaxMessageSizeBytes);
        existing.MaxRecipientsPerMessage = NormalizePositive(policy.MaxRecipientsPerMessage);
        existing.UpdatedUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Submission policy saved. Enabled={Enabled}; MaxMessageSizeBytes={MaxMessageSizeBytes}; MaxRecipientsPerMessage={MaxRecipientsPerMessage}",
            existing.IsEnabled,
            existing.MaxMessageSizeBytes,
            existing.MaxRecipientsPerMessage);

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.Configuration,
            Message = "Submission policy updated."
        }, cancellationToken);
    }

    public TrustedDevicePolicySummary DescribeEffectivePolicy(TrustedNetwork profile, SubmissionPolicy policy)
    {
        return new TrustedDevicePolicySummary
        {
            TrustedNetworkId = profile.Id,
            GlobalPolicyEnabled = policy.IsEnabled,
            MessageSizeLimit = CreateLimitSummary(
                profile.MaxMessageSizeBytes,
                policy.IsEnabled ? policy.MaxMessageSizeBytes : null),
            RecipientLimit = CreateLimitSummary(
                profile.MaxRecipientsPerMessage,
                policy.IsEnabled ? policy.MaxRecipientsPerMessage : null),
            RateLimitMessagesPerHour = profile.RateLimitMessagesPerHour,
            AllowedSenderRuleCount = CountRules(profile.AllowedSenderAddresses)
                + (policy.IsEnabled ? CountRules(policy.AllowedSenderAddresses) : 0),
            BlockedSenderRuleCount = CountRules(profile.BlockedSenderAddresses)
                + (policy.IsEnabled ? CountRules(policy.BlockedSenderAddresses) : 0),
            AllowedRecipientDomainRuleCount = CountRules(profile.AllowedRecipientDomains)
                + (policy.IsEnabled ? CountRules(policy.AllowedRecipientDomains) : 0),
            BlockedRecipientDomainRuleCount = CountRules(profile.BlockedRecipientDomains)
                + (policy.IsEnabled ? CountRules(policy.BlockedRecipientDomains) : 0)
        };
    }

    public SubmissionPolicyDecision CanAcceptFrom(
        TrustedNetwork profile,
        SubmissionPolicy policy,
        string envelopeFrom,
        long declaredSizeBytes)
    {
        var address = NormalizeAddress(envelopeFrom);

        var profileSizeLimit = profile.MaxMessageSizeBytes;
        var globalSizeLimit = policy.IsEnabled ? policy.MaxMessageSizeBytes : null;
        var effectiveSizeLimit = MinPositive(profileSizeLimit, globalSizeLimit);
        if (effectiveSizeLimit is not null && declaredSizeBytes > effectiveSizeLimit.Value)
        {
            return SubmissionPolicyDecision.Deny($"Message size exceeds the allowed limit of {effectiveSizeLimit.Value} bytes.");
        }

        var senderDecision = CheckSenderList(address, profile.BlockedSenderAddresses, allowList: false);
        if (!senderDecision.Allowed)
        {
            return senderDecision;
        }

        if (policy.IsEnabled)
        {
            senderDecision = CheckSenderList(address, policy.BlockedSenderAddresses, allowList: false);
            if (!senderDecision.Allowed)
            {
                return senderDecision;
            }
        }

        senderDecision = CheckSenderList(address, profile.AllowedSenderAddresses, allowList: true);
        if (!senderDecision.Allowed)
        {
            return senderDecision;
        }

        if (policy.IsEnabled)
        {
            senderDecision = CheckSenderList(address, policy.AllowedSenderAddresses, allowList: true);
            if (!senderDecision.Allowed)
            {
                return senderDecision;
            }
        }

        return SubmissionPolicyDecision.Allow();
    }

    public SubmissionPolicyDecision CanDeliverTo(
        TrustedNetwork profile,
        SubmissionPolicy policy,
        string recipient,
        int recipientNumber)
    {
        var address = NormalizeAddress(recipient);
        var domain = GetDomain(address);
        if (string.IsNullOrWhiteSpace(domain))
        {
            return SubmissionPolicyDecision.Deny("Recipient address does not include a domain.");
        }

        var profileRecipientLimit = profile.MaxRecipientsPerMessage;
        var globalRecipientLimit = policy.IsEnabled ? policy.MaxRecipientsPerMessage : null;
        var effectiveRecipientLimit = MinPositive(profileRecipientLimit, globalRecipientLimit);
        if (effectiveRecipientLimit is not null && recipientNumber > effectiveRecipientLimit.Value)
        {
            return SubmissionPolicyDecision.Deny($"Message exceeds the allowed recipient limit of {effectiveRecipientLimit.Value}.");
        }

        var domainDecision = CheckDomainList(domain, profile.BlockedRecipientDomains, allowList: false);
        if (!domainDecision.Allowed)
        {
            return domainDecision;
        }

        if (policy.IsEnabled)
        {
            domainDecision = CheckDomainList(domain, policy.BlockedRecipientDomains, allowList: false);
            if (!domainDecision.Allowed)
            {
                return domainDecision;
            }
        }

        domainDecision = CheckDomainList(domain, profile.AllowedRecipientDomains, allowList: true);
        if (!domainDecision.Allowed)
        {
            return domainDecision;
        }

        if (policy.IsEnabled)
        {
            domainDecision = CheckDomainList(domain, policy.AllowedRecipientDomains, allowList: true);
            if (!domainDecision.Allowed)
            {
                return domainDecision;
            }
        }

        return SubmissionPolicyDecision.Allow();
    }

    private static SubmissionPolicyDecision CheckSenderList(string address, string? list, bool allowList)
    {
        var entries = ParseList(list);
        if (entries.Count == 0)
        {
            return SubmissionPolicyDecision.Allow();
        }

        var matched = entries.Any(entry => MatchesAddress(entry, address));
        if (allowList && !matched)
        {
            return SubmissionPolicyDecision.Deny("Sender is not allowed by submission policy.");
        }

        if (!allowList && matched)
        {
            return SubmissionPolicyDecision.Deny("Sender is blocked by submission policy.");
        }

        return SubmissionPolicyDecision.Allow();
    }

    private static SubmissionPolicyDecision CheckDomainList(string domain, string? list, bool allowList)
    {
        var entries = ParseList(list);
        if (entries.Count == 0)
        {
            return SubmissionPolicyDecision.Allow();
        }

        var matched = entries.Any(entry => MatchesDomain(entry, domain));
        if (allowList && !matched)
        {
            return SubmissionPolicyDecision.Deny("Recipient domain is not allowed by submission policy.");
        }

        if (!allowList && matched)
        {
            return SubmissionPolicyDecision.Deny("Recipient domain is blocked by submission policy.");
        }

        return SubmissionPolicyDecision.Allow();
    }

    private static bool MatchesAddress(string pattern, string address)
    {
        if (pattern.StartsWith("@", StringComparison.Ordinal))
        {
            return address.EndsWith(pattern, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.StartsWith("*@", StringComparison.Ordinal))
        {
            return address.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(pattern, address, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDomain(string pattern, string domain)
    {
        var normalizedPattern = pattern.Trim().TrimStart('@').ToLowerInvariant();
        if (normalizedPattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = normalizedPattern[1..];
            return domain.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(normalizedPattern, domain, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ParseList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split([',', ';', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeAddress(string value)
    {
        var trimmed = value.Trim();
        var start = trimmed.IndexOf('<');
        var end = trimmed.IndexOf('>');
        if (start >= 0 && end > start)
        {
            trimmed = trimmed[(start + 1)..end];
        }

        return trimmed.Trim().Trim('"').ToLowerInvariant();
    }

    private static string? GetDomain(string address)
    {
        var at = address.LastIndexOf('@');
        return at < 0 || at == address.Length - 1 ? null : address[(at + 1)..];
    }

    private static string? NormalizeList(string? value)
    {
        var entries = ParseList(value);
        return entries.Count == 0 ? null : string.Join(Environment.NewLine, entries);
    }

    private static long? NormalizePositive(long? value) => value is > 0 ? value : null;

    private static int? NormalizePositive(int? value) => value is > 0 ? value : null;

    private static long? MinPositive(long? left, long? right)
    {
        return (left, right) switch
        {
            ({ } l, { } r) => Math.Min(l, r),
            ({ } l, null) => l,
            (null, { } r) => r,
            _ => null
        };
    }

    private static int? MinPositive(int? left, int? right)
    {
        return (left, right) switch
        {
            ({ } l, { } r) => Math.Min(l, r),
            ({ } l, null) => l,
            (null, { } r) => r,
            _ => null
        };
    }

    private static EffectivePolicyLimit CreateLimitSummary(long? profileValue, long? globalValue)
    {
        var value = MinPositive(profileValue, globalValue);
        return new EffectivePolicyLimit
        {
            Value = value,
            Source = DescribeLimitSource(profileValue, globalValue)
        };
    }

    private static EffectivePolicyLimit CreateLimitSummary(int? profileValue, int? globalValue)
    {
        var value = MinPositive(profileValue, globalValue);
        return new EffectivePolicyLimit
        {
            Value = value,
            Source = DescribeLimitSource(profileValue, globalValue)
        };
    }

    private static string DescribeLimitSource(long? profileValue, long? globalValue)
    {
        return (profileValue, globalValue) switch
        {
            (null, null) => "Not set",
            ({ }, null) => "Trusted device",
            (null, { }) => "Global policy",
            ({ } profile, { } global) when profile < global => "Trusted device (stricter)",
            ({ } profile, { } global) when global < profile => "Global policy (stricter)",
            _ => "Trusted device + global policy"
        };
    }

    private static string DescribeLimitSource(int? profileValue, int? globalValue)
    {
        return DescribeLimitSource(
            profileValue is null ? null : (long?)profileValue.Value,
            globalValue is null ? null : (long?)globalValue.Value);
    }

    private static int CountRules(string? value)
    {
        return ParseList(value).Count;
    }
}
