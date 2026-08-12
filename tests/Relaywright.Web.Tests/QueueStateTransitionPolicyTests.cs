using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Queueing;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class QueueStateTransitionPolicyTests
{
    [Fact]
    public void TransitionMatrixAllowsOnlyDefinedQueueLifecycleChanges()
    {
        var allowed = new HashSet<(QueuedMessageStatus Current, QueuedMessageStatus Next)>
        {
            (QueuedMessageStatus.Pending, QueuedMessageStatus.InProgress),
            (QueuedMessageStatus.Pending, QueuedMessageStatus.RetryScheduled),
            (QueuedMessageStatus.Pending, QueuedMessageStatus.Expired),
            (QueuedMessageStatus.InProgress, QueuedMessageStatus.InProgress),
            (QueuedMessageStatus.InProgress, QueuedMessageStatus.RetryScheduled),
            (QueuedMessageStatus.InProgress, QueuedMessageStatus.Delivered),
            (QueuedMessageStatus.InProgress, QueuedMessageStatus.Failed),
            (QueuedMessageStatus.InProgress, QueuedMessageStatus.Expired),
            (QueuedMessageStatus.RetryScheduled, QueuedMessageStatus.InProgress),
            (QueuedMessageStatus.RetryScheduled, QueuedMessageStatus.RetryScheduled),
            (QueuedMessageStatus.RetryScheduled, QueuedMessageStatus.Expired),
            (QueuedMessageStatus.Failed, QueuedMessageStatus.RetryScheduled)
        };

        foreach (var current in Enum.GetValues<QueuedMessageStatus>())
        {
            foreach (var next in Enum.GetValues<QueuedMessageStatus>())
            {
                Assert.Equal(
                    allowed.Contains((current, next)),
                    QueueStateTransitionPolicy.CanTransition(current, next));
            }
        }
    }

    [Fact]
    public void EnsureAllowedRejectsTerminalStateMutation()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            QueueStateTransitionPolicy.EnsureAllowed(
                QueuedMessageStatus.Delivered,
                QueuedMessageStatus.RetryScheduled));

        Assert.Contains("Delivered", exception.Message, StringComparison.Ordinal);
        Assert.Contains("RetryScheduled", exception.Message, StringComparison.Ordinal);
    }
}
