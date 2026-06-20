namespace Relaywright.Web.Data.Entities;

public sealed class QueuedMessage
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");

    public string? RemoteIpAddress { get; set; }

    public string EnvelopeFrom { get; set; } = string.Empty;

    public long MessageSizeBytes { get; set; }

    public string SpoolFileRelativePath { get; set; } = string.Empty;

    public QueuedMessageStatus Status { get; set; } = QueuedMessageStatus.Pending;

    public int AttemptCount { get; set; }

    public string? LastResponseCode { get; set; }

    public string? LastResponseText { get; set; }

    public string? LastError { get; set; }

    public DeliveryFailureCategory FailureCategory { get; set; } = DeliveryFailureCategory.None;

    public DateTimeOffset AcceptedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset NextAttemptAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastAttemptStartedUtc { get; set; }

    public DateTimeOffset? LastAttemptCompletedUtc { get; set; }

    public DateTimeOffset? DeliveredUtc { get; set; }

    public DateTimeOffset ExpiresUtc { get; set; } = DateTimeOffset.UtcNow.AddHours(72);

    public ICollection<QueuedMessageRecipient> Recipients { get; set; } = new List<QueuedMessageRecipient>();

    public ICollection<DeliveryAttempt> DeliveryAttempts { get; set; } = new List<DeliveryAttempt>();
}

