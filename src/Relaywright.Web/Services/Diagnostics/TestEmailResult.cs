namespace Relaywright.Web.Services.Diagnostics;

public sealed class TestEmailResult
{
    public bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;

    public Guid SessionId { get; init; }
}
