using Relaywright.Web.Configuration;
using Relaywright.Web.Services.Queueing;

namespace Relaywright.Web.Services.Delivery;

public interface IUpstreamDeliveryService
{
    Task<DeliveryResult> DeliverAsync(
        DeliveryWorkItem workItem,
        RelayConfigurationSnapshot configuration,
        CancellationToken cancellationToken);
}

