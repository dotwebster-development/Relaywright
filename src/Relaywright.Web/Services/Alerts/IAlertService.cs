using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Alerts;

public interface IAlertService
{
    Task<IReadOnlyList<AlertRule>> GetRulesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AlertResult>> GetRecentResultsAsync(int count, CancellationToken cancellationToken);

    Task SaveRuleAsync(AlertRule rule, CancellationToken cancellationToken);

    Task EvaluateAsync(CancellationToken cancellationToken);
}
