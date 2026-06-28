namespace Relaywright.Web.Services.Runtime;

public sealed class RuntimeComponentState
{
    public string Status { get; init; } = "Unknown";

    public DateTimeOffset? LastChangedUtc { get; init; }

    public DateTimeOffset? LastHeartbeatUtc { get; init; }

    public string? Detail { get; init; }

    public string? LastError { get; init; }
}
