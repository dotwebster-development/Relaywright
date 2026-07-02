namespace Relaywright.Web.Data.Entities;

public sealed class RuntimeControlState
{
    public int Id { get; set; } = 1;

    public bool IsDeliveryPaused { get; set; }

    public string? DeliveryPauseReason { get; set; }

    public string? DeliveryPausedBy { get; set; }

    public DateTimeOffset? DeliveryPausedUtc { get; set; }

    public bool RestartRequired { get; set; }

    public string? RestartReason { get; set; }

    public string? RestartRequestedBy { get; set; }

    public DateTimeOffset? RestartRequestedUtc { get; set; }

    public bool RestartSupported { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
