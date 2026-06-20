using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Queueing;

public sealed class DeliveryResult
{
    public bool Succeeded { get; init; }

    public bool IsPermanentFailure { get; init; }

    public DeliveryFailureCategory FailureCategory { get; init; } = DeliveryFailureCategory.None;

    public string? ResponseCode { get; init; }

    public string? ResponseText { get; init; }

    public string? ExceptionType { get; init; }

    public string? ErrorDetail { get; init; }
}

