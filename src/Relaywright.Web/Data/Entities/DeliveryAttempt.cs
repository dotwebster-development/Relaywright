namespace Relaywright.Web.Data.Entities;

public sealed class DeliveryAttempt
{
    public int Id { get; set; }

    public Guid QueuedMessageId { get; set; }

    public int AttemptNumber { get; set; }

    public DateTimeOffset StartedUtc { get; set; }

    public DateTimeOffset? CompletedUtc { get; set; }

    public bool Succeeded { get; set; }

    public DeliveryFailureCategory FailureCategory { get; set; } = DeliveryFailureCategory.None;

    public string? ResponseCode { get; set; }

    public string? ResponseText { get; set; }

    public string? ExceptionType { get; set; }

    public string? ExceptionMessage { get; set; }

    public QueuedMessage QueuedMessage { get; set; } = null!;
}

