namespace Relaywright.Web.Services.Runtime;

public sealed record DashboardReadinessSnapshot(IReadOnlyList<DashboardReadinessItem> Items)
{
    public static DashboardReadinessSnapshot Empty { get; } = new([]);

    public int RequiredCount => Items.Count(x => x.IsRequired);

    public int CompletedRequiredCount => Items.Count(x => x.IsRequired && x.IsComplete);

    public bool IsReady => RequiredCount > 0 && CompletedRequiredCount == RequiredCount;

    public int CompletionPercent => RequiredCount == 0
        ? 0
        : (int)Math.Round(CompletedRequiredCount * 100d / RequiredCount);
}

public sealed record DashboardReadinessItem(
    string Key,
    string Label,
    string Status,
    string Detail,
    string BadgeClass,
    string Page,
    string ActionLabel,
    bool IsComplete,
    bool IsRequired = true);
