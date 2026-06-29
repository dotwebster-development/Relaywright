namespace Relaywright.Web.Services.Diagnostics;

public sealed class SubmissionFlowCheckRequest
{
    public string SourceIpAddress { get; init; } = string.Empty;

    public string EnvelopeFrom { get; init; } = string.Empty;

    public string Recipients { get; init; } = string.Empty;

    public long DeclaredSizeBytes { get; init; }
}
