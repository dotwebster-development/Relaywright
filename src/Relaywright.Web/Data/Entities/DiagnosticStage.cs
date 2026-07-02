namespace Relaywright.Web.Data.Entities;

public sealed class DiagnosticStage
{
    public long Id { get; set; }

    public Guid DiagnosticRunId { get; set; }

    public int Sequence { get; set; }

    public string Name { get; set; } = string.Empty;

    public DiagnosticStageStatus Status { get; set; } = DiagnosticStageStatus.Running;

    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedUtc { get; set; }

    public long? ElapsedMilliseconds { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? Detail { get; set; }

    public DiagnosticRun DiagnosticRun { get; set; } = null!;
}
