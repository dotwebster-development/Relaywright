using Relaywright.Web.Configuration;
using Relaywright.Web.Services.Delivery;
using Relaywright.Web.Services.Queueing;

namespace Relaywright.Web.Tests.Support;

internal sealed class RecordingUpstreamDeliveryService : IUpstreamDeliveryService
{
    public DeliveryResult Result { get; set; } = new()
    {
        Succeeded = true,
        ResponseText = "250 queued"
    };

    public Exception? Exception { get; set; }

    public List<DeliveryWorkItem> WorkItems { get; } = new();

    public Task<DeliveryResult> DeliverAsync(
        DeliveryWorkItem workItem,
        RelayConfigurationSnapshot configuration,
        CancellationToken cancellationToken)
    {
        WorkItems.Add(workItem);
        if (Exception is not null)
        {
            throw Exception;
        }

        return Task.FromResult(Result);
    }
}
