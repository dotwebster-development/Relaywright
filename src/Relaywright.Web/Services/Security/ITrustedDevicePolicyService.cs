using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Security;

public interface ITrustedDevicePolicyService
{
    Task<SubmissionPolicy> GetPolicyAsync(CancellationToken cancellationToken);

    Task SavePolicyAsync(SubmissionPolicy policy, CancellationToken cancellationToken);

    TrustedDevicePolicySummary DescribeEffectivePolicy(TrustedNetwork profile, SubmissionPolicy policy);

    SubmissionPolicyDecision CanAcceptFrom(
        TrustedNetwork profile,
        SubmissionPolicy policy,
        string envelopeFrom,
        long declaredSizeBytes);

    SubmissionPolicyDecision CanDeliverTo(
        TrustedNetwork profile,
        SubmissionPolicy policy,
        string recipient,
        int recipientNumber);
}
