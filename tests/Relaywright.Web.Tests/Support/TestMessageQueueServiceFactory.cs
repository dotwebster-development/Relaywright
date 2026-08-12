using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Data;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;

namespace Relaywright.Web.Tests.Support;

internal static class TestMessageQueueServiceFactory
{
    public static MessageQueueService Create(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IOperationalEventService eventService,
        IQueueSignal queueSignal,
        DatabaseConfiguration databaseConfiguration,
        TimeProvider? timeProvider = null,
        QueueProcessingOptions? queueOptions = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var retryDelayCalculator = new RetryDelayCalculator();
        var claimService = new QueueClaimService(
            dbContextFactory,
            databaseConfiguration,
            Microsoft.Extensions.Options.Options.Create(queueOptions ?? new QueueProcessingOptions()),
            NullLogger<QueueClaimService>.Instance,
            clock);
        var deliveryStateService = new QueueDeliveryStateService(
            dbContextFactory,
            retryDelayCalculator,
            eventService,
            NullLogger<QueueDeliveryStateService>.Instance,
            clock);

        return new MessageQueueService(
            dbContextFactory,
            eventService,
            queueSignal,
            claimService,
            deliveryStateService,
            NullLogger<MessageQueueService>.Instance);
    }
}
