namespace Relaywright.Web.Services.Diagnostics;

public sealed class SubmissionFlowCheckResult
{
    public bool Succeeded { get; init; }

    public Guid DiagnosticRunId { get; init; }

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<SubmissionFlowCheckStage> Stages { get; init; } = Array.Empty<SubmissionFlowCheckStage>();
}

public sealed class SubmissionFlowCheckStage
{
    public string Name { get; init; } = string.Empty;

    public bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;
}
