namespace Relaywright.Web.Services.Diagnostics;

public sealed class ConnectivityTestResult
{
    public bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;

    public Guid? DiagnosticRunId { get; init; }
}
