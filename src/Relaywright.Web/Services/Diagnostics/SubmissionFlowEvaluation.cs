namespace Relaywright.Web.Services.Diagnostics;

public sealed class SubmissionFlowEvaluation
{
    public bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? Recommendation { get; init; }

    public IReadOnlyList<SubmissionFlowCheckStage> Stages { get; init; } = Array.Empty<SubmissionFlowCheckStage>();
}
