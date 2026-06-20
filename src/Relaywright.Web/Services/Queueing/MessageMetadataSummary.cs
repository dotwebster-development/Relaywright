namespace Relaywright.Web.Services.Queueing;

public sealed class MessageMetadataSummary
{
    public string? Subject { get; init; }

    public string? MessageId { get; init; }

    public DateTimeOffset? Date { get; init; }

    public string? HeaderFrom { get; init; }

    public string? HeaderTo { get; init; }

    public string? ContentType { get; init; }
}

