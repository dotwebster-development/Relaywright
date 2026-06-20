namespace Relaywright.Web.Services.Events;

public interface IOperationalEventService
{
    Task WriteAsync(OperationalEventRequest request, CancellationToken cancellationToken = default);
}

