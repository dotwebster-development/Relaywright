using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Tests.Support;

internal sealed class RecordingOperationalEventService : IOperationalEventService
{
    public List<OperationalEventRequest> Events { get; } = new();

    public Task WriteAsync(OperationalEventRequest request, CancellationToken cancellationToken = default)
    {
        Events.Add(request);
        return Task.CompletedTask;
    }
}
