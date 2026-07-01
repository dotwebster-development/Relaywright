using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Diagnostics;

public sealed class SubmissionFlowChecker(
    ISubmissionFlowEvaluator submissionFlowEvaluator,
    IDiagnosticRunRecorder diagnosticRunRecorder,
    ILogger<SubmissionFlowChecker> logger) : ISubmissionFlowChecker
{
    public async Task<SubmissionFlowCheckResult> CheckAsync(
        SubmissionFlowCheckRequest request,
        string? requestedBy,
        CancellationToken cancellationToken)
    {
        var run = await diagnosticRunRecorder.StartRunAsync(
            DiagnosticRunKind.SubmissionFlow,
            sessionId: null,
            requestedBy,
            cancellationToken);
        try
        {
            var evaluation = await submissionFlowEvaluator.EvaluateAsync(request, cancellationToken);
            await PersistStagesAsync(run.Id, evaluation.Stages, cancellationToken);
            await diagnosticRunRecorder.CompleteRunAsync(run.Id, evaluation.Succeeded, evaluation.Message, cancellationToken);
            logger.LogInformation(
                "Submission flow check completed. Succeeded={Succeeded}; SourceIp={SourceIp}; RequestedBy={RequestedBy}",
                evaluation.Succeeded,
                request.SourceIpAddress,
                requestedBy);

            return Result(evaluation, run.Id);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Submission flow check failed unexpectedly. SourceIp={SourceIp}; RequestedBy={RequestedBy}",
                request.SourceIpAddress,
                requestedBy);
            await diagnosticRunRecorder.CompleteRunAsync(run.Id, false, exception.Message, cancellationToken);
            return new SubmissionFlowCheckResult
            {
                Succeeded = false,
                DiagnosticRunId = run.Id,
                Message = exception.Message,
                Recommendation = "Review the application logs for the failed diagnostic run."
            };
        }
    }

    private async Task PersistStagesAsync(
        Guid runId,
        IReadOnlyList<SubmissionFlowCheckStage> stages,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < stages.Count; index++)
        {
            var stageResult = stages[index];
            var stage = await diagnosticRunRecorder.StartStageAsync(
                runId,
                index + 1,
                stageResult.Name,
                stageResult.Message,
                cancellationToken);
            await diagnosticRunRecorder.CompleteStageAsync(
                stage.Id,
                stageResult.Succeeded ? DiagnosticStageStatus.Succeeded : DiagnosticStageStatus.Failed,
                stageResult.Message,
                detail: null,
                cancellationToken);
        }
    }

    private static SubmissionFlowCheckResult Result(SubmissionFlowEvaluation evaluation, Guid runId)
    {
        return new SubmissionFlowCheckResult
        {
            Succeeded = evaluation.Succeeded,
            DiagnosticRunId = runId,
            Message = evaluation.Message,
            Recommendation = evaluation.Recommendation,
            Stages = evaluation.Stages.ToArray()
        };
    }
}
