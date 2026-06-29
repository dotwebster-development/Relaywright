namespace Relaywright.Web.Services.Diagnostics;

public interface ISubmissionFlowChecker
{
    Task<SubmissionFlowCheckResult> CheckAsync(
        SubmissionFlowCheckRequest request,
        string? requestedBy,
        CancellationToken cancellationToken);
}
