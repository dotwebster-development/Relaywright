namespace Relaywright.Web.Data.Entities;

public sealed class OperationalEvent
{
    public long Id { get; set; }

    public DateTimeOffset OccurredUtc { get; set; } = DateTimeOffset.UtcNow;

    public EventSeverity Severity { get; set; } = EventSeverity.Information;

    public OperationalEventCategory Category { get; set; } = OperationalEventCategory.System;

    public Guid? SessionId { get; set; }

    public Guid? QueuedMessageId { get; set; }

    public string? RemoteIpAddress { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? Detail { get; set; }
}

