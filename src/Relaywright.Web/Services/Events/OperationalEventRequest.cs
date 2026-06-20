using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Events;

public sealed class OperationalEventRequest
{
    public EventSeverity Severity { get; init; } = EventSeverity.Information;

    public OperationalEventCategory Category { get; init; } = OperationalEventCategory.System;

    public Guid? SessionId { get; init; }

    public Guid? QueuedMessageId { get; init; }

    public string? RemoteIpAddress { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? Detail { get; init; }
}

