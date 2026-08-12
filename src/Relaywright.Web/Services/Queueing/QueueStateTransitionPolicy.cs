using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Queueing;

public static class QueueStateTransitionPolicy
{
    public static bool CanTransition(QueuedMessageStatus current, QueuedMessageStatus next)
    {
        return (current, next) switch
        {
            (QueuedMessageStatus.Pending, QueuedMessageStatus.InProgress) => true,
            (QueuedMessageStatus.Pending, QueuedMessageStatus.RetryScheduled) => true,
            (QueuedMessageStatus.Pending, QueuedMessageStatus.Expired) => true,
            (QueuedMessageStatus.InProgress, QueuedMessageStatus.InProgress) => true,
            (QueuedMessageStatus.InProgress, QueuedMessageStatus.RetryScheduled) => true,
            (QueuedMessageStatus.InProgress, QueuedMessageStatus.Delivered) => true,
            (QueuedMessageStatus.InProgress, QueuedMessageStatus.Failed) => true,
            (QueuedMessageStatus.InProgress, QueuedMessageStatus.Expired) => true,
            (QueuedMessageStatus.RetryScheduled, QueuedMessageStatus.InProgress) => true,
            (QueuedMessageStatus.RetryScheduled, QueuedMessageStatus.RetryScheduled) => true,
            (QueuedMessageStatus.RetryScheduled, QueuedMessageStatus.Expired) => true,
            (QueuedMessageStatus.Failed, QueuedMessageStatus.RetryScheduled) => true,
            _ => false
        };
    }

    public static void EnsureAllowed(QueuedMessageStatus current, QueuedMessageStatus next)
    {
        if (!CanTransition(current, next))
        {
            throw new InvalidOperationException($"Queue transition from {current} to {next} is not allowed.");
        }
    }
}
