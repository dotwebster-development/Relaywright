using Relaywright.Web.Services.Queueing;

namespace Relaywright.Web.Tests.Support;

internal sealed class RecordingQueueSignal : IQueueSignal
{
    public int PulseCount { get; private set; }

    public void Pulse()
    {
        PulseCount += 1;
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
