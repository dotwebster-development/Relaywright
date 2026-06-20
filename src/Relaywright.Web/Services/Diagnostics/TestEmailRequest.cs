namespace Relaywright.Web.Services.Diagnostics;

public sealed class TestEmailRequest
{
    public string FromAddress { get; init; } = string.Empty;

    public string ToAddress { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;
}
