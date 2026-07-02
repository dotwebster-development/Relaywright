namespace Relaywright.Web.Services.Runtime;

public sealed class ApplicationRestartRequestResult
{
    public bool RestartScheduled { get; init; }

    public bool RestartSupported { get; init; }

    public string Message { get; init; } = string.Empty;
}
