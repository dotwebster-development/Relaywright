namespace Relaywright.Web.Data.Entities;

public enum QueuedMessageStatus
{
    Pending = 0,
    InProgress = 1,
    RetryScheduled = 2,
    Delivered = 3,
    Failed = 4,
    Expired = 5
}

