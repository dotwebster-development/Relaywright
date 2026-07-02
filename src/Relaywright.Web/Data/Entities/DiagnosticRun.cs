namespace Relaywright.Web.Data.Entities;

public sealed class DiagnosticRun
{
    public Guid Id { get; set; }

    public DiagnosticRunKind Kind { get; set; }

    public Guid? SessionId { get; set; }

    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedUtc { get; set; }

    public bool? Succeeded { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? RequestedBy { get; set; }

    public ICollection<DiagnosticStage> Stages { get; set; } = new List<DiagnosticStage>();
}
