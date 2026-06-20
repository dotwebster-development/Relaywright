namespace Relaywright.Web.Services.Queueing;

public sealed class NewQueuedMessageRequest
{
    public Guid MessageId { get; init; }

    public Guid SessionId { get; init; }

    public string? RemoteIpAddress { get; init; }

    public string EnvelopeFrom { get; init; } = string.Empty;

    public IReadOnlyList<string> Recipients { get; init; } = Array.Empty<string>();

    public string SpoolFileRelativePath { get; init; } = string.Empty;

    public long MessageSizeBytes { get; init; }

    public DateTimeOffset AcceptedUtc { get; init; } = DateTimeOffset.UtcNow;

    public int MessageExpirationHours { get; init; } = 72;
}

