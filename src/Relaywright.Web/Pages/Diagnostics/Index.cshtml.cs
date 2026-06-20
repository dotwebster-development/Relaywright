using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Diagnostics;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Relay;

namespace Relaywright.Web.Pages.Diagnostics;

public sealed class IndexModel(
    IRelayConfigurationService relayConfigurationService,
    IUpstreamConnectivityTester upstreamConnectivityTester,
    IOperationalEventService eventService,
    ILogger<IndexModel> logger) : PageModel
{
    public RelayConfigurationSnapshot Configuration { get; private set; } = new();

    public ConnectivityTestResult? Result { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Configuration = await relayConfigurationService.GetSnapshotAsync(cancellationToken);
        logger.LogDebug(
            "Diagnostics page loaded. UpstreamConfigured={UpstreamConfigured}; User={UserName}",
            !string.IsNullOrWhiteSpace(Configuration.UpstreamHost),
            User.Identity?.Name);
    }

    public async Task OnPostAsync(CancellationToken cancellationToken)
    {
        Configuration = await relayConfigurationService.GetSnapshotAsync(cancellationToken);
        logger.LogInformation(
            "Upstream connectivity test requested from admin page. UpstreamHost={UpstreamHost}; UpstreamPort={UpstreamPort}; User={UserName}",
            Configuration.UpstreamHost,
            Configuration.UpstreamPort,
            User.Identity?.Name);

        Result = await upstreamConnectivityTester.TestAsync(Configuration, cancellationToken);

        logger.LogInformation(
            "Upstream connectivity test completed from admin page. Succeeded={Succeeded}; Message={Message}; User={UserName}",
            Result.Succeeded,
            Result.Message,
            User.Identity?.Name);

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Severity = Result.Succeeded ? EventSeverity.Information : EventSeverity.Warning,
            Category = OperationalEventCategory.Diagnostics,
            Message = Result.Succeeded
                ? "Upstream connectivity test succeeded."
                : "Upstream connectivity test failed.",
            Detail = Result.Message
        }, cancellationToken);
    }
}
