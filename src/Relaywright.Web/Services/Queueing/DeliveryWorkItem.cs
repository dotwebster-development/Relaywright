namespace Relaywright.Web.Services.Queueing;

public sealed class DeliveryWorkItem
{
    public Guid MessageId { get; init; }

    public int DeliveryAttemptId { get; init; }

    public int AttemptNumber { get; init; }

    public string CorrelationId { get; init; } = string.Empty;

    public string EnvelopeFrom { get; init; } = string.Empty;

    public IReadOnlyList<string> Recipients { get; init; } = Array.Empty<string>();

    public string SpoolFileRelativePath { get; init; } = string.Empty;

    public string? RemoteIpAddress { get; init; }

    public DateTimeOffset ExpiresUtc { get; init; }
}

