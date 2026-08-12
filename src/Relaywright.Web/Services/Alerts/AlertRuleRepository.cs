using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Validation;

namespace Relaywright.Web.Services.Alerts;

public sealed class AlertRuleRepository(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IOperationalEventService eventService,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<AlertRule>> GetRulesAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.AlertRules
            .AsNoTracking()
            .OrderBy(rule => rule.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AlertResult>> GetRecentResultsAsync(
        int count,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var results = await dbContext.AlertResults
            .AsNoTracking()
            .Include(result => result.AlertRule)
            .ToListAsync(cancellationToken);

        return results
            .OrderByDescending(result => result.OccurredUtc)
            .Take(Math.Max(1, count))
            .ToList();
    }

    public async Task SaveRuleAsync(AlertRule rule, CancellationToken cancellationToken)
    {
        ValidateRule(rule);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.AlertRules.SingleOrDefaultAsync(
            existingRule => existingRule.Id == rule.Id,
            cancellationToken)
            ?? throw new InvalidOperationException("Alert rule not found.");

        existing.IsEnabled = rule.IsEnabled;
        existing.Threshold = rule.Threshold;
        existing.CooldownMinutes = rule.CooldownMinutes;
        existing.EmailRecipients = string.IsNullOrWhiteSpace(rule.EmailRecipients)
            ? null
            : Trim(rule.EmailRecipients, ValidationLimits.MaximumTextLength);
        existing.UpdatedUtc = clock.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.Alert,
            Message = $"Alert rule updated: {existing.DisplayName}."
        }, cancellationToken);
    }

    private static void ValidateRule(AlertRule rule)
    {
        if (rule.Id < 1)
        {
            throw new InvalidOperationException("Alert rule ID must be at least 1.");
        }

        if (rule.Threshold < 0)
        {
            throw new InvalidOperationException("Alert threshold must be zero or greater.");
        }

        if (rule.CooldownMinutes < 1)
        {
            throw new InvalidOperationException("Alert cooldown must be at least 1 minute.");
        }

        if (string.IsNullOrWhiteSpace(rule.EmailRecipients))
        {
            return;
        }

        if (rule.EmailRecipients.Length > ValidationLimits.MaximumTextLength)
        {
            throw new InvalidOperationException(
                ValidationMessages.MaximumLength("Alert email recipients", ValidationLimits.MaximumTextLength));
        }

        if (ValidationRules.ContainsDisallowedControlCharacter(
            rule.EmailRecipients,
            allowLineBreaks: true,
            out _))
        {
            throw new InvalidOperationException(
                ValidationMessages.UnsupportedControlCharacter("Alert email recipients"));
        }

        foreach (var recipient in ValidationRules.SplitDelimitedList(rule.EmailRecipients))
        {
            if (!ValidationRules.IsMailboxAddress(recipient))
            {
                throw new InvalidOperationException(
                    $"Alert email recipients contains an invalid mailbox address: {recipient}.");
            }
        }
    }

    private static string Trim(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
