using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Diagnostics;

public interface IDiagnosticRunRecorder
{
    Task<DiagnosticRun> StartRunAsync(
        DiagnosticRunKind kind,
        Guid? sessionId,
        string? requestedBy,
        CancellationToken cancellationToken);

    Task<DiagnosticStage> StartStageAsync(
        Guid runId,
        int sequence,
        string name,
        string message,
        CancellationToken cancellationToken);

    Task CompleteStageAsync(
        long stageId,
        DiagnosticStageStatus status,
        string message,
        string? detail,
        CancellationToken cancellationToken);

    Task CompleteRunAsync(Guid runId, bool succeeded, string message, CancellationToken cancellationToken);

    Task<IReadOnlyList<DiagnosticRun>> GetRecentRunsAsync(
        DiagnosticRunKind? kind,
        int count,
        CancellationToken cancellationToken);

    Task<DiagnosticRun?> GetRunAsync(Guid id, CancellationToken cancellationToken);
}
