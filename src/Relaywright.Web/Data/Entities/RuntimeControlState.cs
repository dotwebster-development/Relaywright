namespace Relaywright.Web.Data.Entities;

public sealed class RuntimeControlState
{
    public int Id { get; set; } = 1;

    public bool IsDeliveryPaused { get; set; }

    public string? DeliveryPauseReason { get; set; }

    public string? DeliveryPausedBy { get; set; }

    public DateTimeOffset? DeliveryPausedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
