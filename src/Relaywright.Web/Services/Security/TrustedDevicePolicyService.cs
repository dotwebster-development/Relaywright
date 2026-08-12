using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.Security;

public sealed class TrustedDevicePolicyService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IOperationalEventService eventService,
    SubmissionPolicyEvaluator policyEvaluator,
    SubmissionPolicyValidator policyValidator,
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
        policyValidator.Validate(policy);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.SubmissionPolicies.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (existing is null)
        {
            existing = new SubmissionPolicy();
            dbContext.SubmissionPolicies.Add(existing);
        }

        existing.IsEnabled = policy.IsEnabled;
        existing.AllowedSenderAddresses = policyValidator.NormalizeList(policy.AllowedSenderAddresses);
        existing.BlockedSenderAddresses = policyValidator.NormalizeList(policy.BlockedSenderAddresses);
        existing.AllowedRecipientDomains = policyValidator.NormalizeList(policy.AllowedRecipientDomains);
        existing.BlockedRecipientDomains = policyValidator.NormalizeList(policy.BlockedRecipientDomains);
        existing.MaxMessageSizeBytes = policyValidator.NormalizePositive(policy.MaxMessageSizeBytes);
        existing.MaxRecipientsPerMessage = policyValidator.NormalizePositive(policy.MaxRecipientsPerMessage);
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
            Message = OperationalEventMessages.SubmissionPolicyUpdated
        }, cancellationToken);
    }

    public SubmissionPolicyDecision CanAcceptFrom(
        TrustedNetwork profile,
        SubmissionPolicy policy,
        string envelopeFrom,
        long declaredSizeBytes)
    {
        return policyEvaluator.CanAcceptFrom(profile, policy, envelopeFrom, declaredSizeBytes);
    }

    public SubmissionPolicyDecision CanDeliverTo(
        TrustedNetwork profile,
        SubmissionPolicy policy,
        string recipient,
        int recipientNumber)
    {
        return policyEvaluator.CanDeliverTo(profile, policy, recipient, recipientNumber);
    }
}
