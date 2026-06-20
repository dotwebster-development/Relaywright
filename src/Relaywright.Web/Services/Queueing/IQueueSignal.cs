namespace Relaywright.Web.Services.Queueing;

public interface IQueueSignal
{
    void Pulse();

    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

