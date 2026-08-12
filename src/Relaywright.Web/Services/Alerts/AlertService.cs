using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Services.Alerts;

public sealed class AlertService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IRuntimeStatusService runtimeStatusService,
    IRelayConfigurationService relayConfigurationService,
    IAdminHttpsCertificateService adminHttpsCertificateService,
    AlertRuleRepository ruleRepository,
    AlertEvaluator evaluator,
    AlertStateCoordinator stateCoordinator,
    TimeProvider? timeProvider = null) : IAlertService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public Task<IReadOnlyList<AlertRule>> GetRulesAsync(CancellationToken cancellationToken)
    {
        return ruleRepository.GetRulesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<AlertResult>> GetRecentResultsAsync(
        int count,
        CancellationToken cancellationToken)
    {
        return ruleRepository.GetRecentResultsAsync(count, cancellationToken);
    }

    public Task SaveRuleAsync(
        AlertRule rule,
        CancellationToken cancellationToken)
    {
        return ruleRepository.SaveRuleAsync(rule, cancellationToken);
    }

    public async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var runtimeStatus = await runtimeStatusService.GetSnapshotAsync(cancellationToken);
        var relayConfiguration = await relayConfigurationService.GetSnapshotAsync(cancellationToken);
        var adminCertificate = await adminHttpsCertificateService.GetConfigurationAsync(cancellationToken);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rules = await dbContext.AlertRules
            .OrderBy(rule => rule.Key)
            .ToListAsync(cancellationToken);

        foreach (var rule in rules.Where(rule => rule.IsEnabled))
        {
            var evaluation = await evaluator.EvaluateAsync(
                dbContext,
                rule,
                runtimeStatus,
                adminCertificate,
                now,
                cancellationToken);

            await stateCoordinator.ApplyAsync(
                dbContext,
                rule,
                evaluation,
                relayConfiguration,
                now,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
