using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.ConfigurationHistory;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Pages.Settings;

public sealed class WebHttpsModel(
    IAdminWebListenerConfigurationService adminWebListenerConfigurationService,
    IConfigurationSnapshotService configurationSnapshotService,
    IApplicationRestartService applicationRestartService,
    IOperationalEventService eventService,
    ILogger<WebHttpsModel> logger) : PageModel
{
    [BindProperty]
    public ListenerInputModel ListenerInput { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public AdminWebListenerConfiguration? CurrentListener { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPageStateAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveListenerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await configurationSnapshotService.CaptureAsync(
                ConfigurationSnapshotService.AdminWebListenerArea,
                User.Identity?.Name,
                "Snapshot before web listener settings save.",
                cancellationToken);
            CurrentListener = await adminWebListenerConfigurationService.SaveAsync(new AdminWebListenerConfiguration
            {
                HttpsPort = ListenerInput.HttpsPort,
                EnableHttp = ListenerInput.EnableHttp,
                HttpPort = ListenerInput.HttpPort
            }, cancellationToken);

            var restart = await applicationRestartService.RequestRestartAsync(
                "Admin web listener settings changed.",
                User.Identity?.Name,
                cancellationToken);
            StatusMessage = $"Web listener settings saved. {restart.Message}";
            await eventService.WriteAsync(new OperationalEventRequest
            {
                Category = OperationalEventCategory.Configuration,
                Message = $"Admin web listener configured for HTTPS port {CurrentListener.HttpsPort} with HTTP {(CurrentListener.EnableHttp ? $"enabled on port {CurrentListener.HttpPort}" : "disabled")}.",
                RemoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            }, cancellationToken);

            logger.LogInformation(
                "Admin web listener settings saved. HttpsPort={HttpsPort}; HttpEnabled={HttpEnabled}; HttpPort={HttpPort}; User={UserName}; RemoteIp={RemoteIp}",
                CurrentListener.HttpsPort,
                CurrentListener.EnableHttp,
                CurrentListener.HttpPort,
                User.Identity?.Name,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            return RedirectToPage();
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Admin web listener settings save failed. HttpsPort={HttpsPort}; HttpEnabled={HttpEnabled}; HttpPort={HttpPort}; User={UserName}; RemoteIp={RemoteIp}",
                ListenerInput.HttpsPort,
                ListenerInput.EnableHttp,
                ListenerInput.HttpPort,
                User.Identity?.Name,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            await LoadPageStateAsync(cancellationToken);
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    private async Task LoadPageStateAsync(CancellationToken cancellationToken)
    {
        CurrentListener = await adminWebListenerConfigurationService.GetConfigurationAsync(cancellationToken);
        var listener = CurrentListener ?? new AdminWebListenerConfiguration();
        ListenerInput = new ListenerInputModel
        {
            HttpsPort = listener.HttpsPort,
            EnableHttp = listener.EnableHttp,
            HttpPort = listener.HttpPort
        };
    }

    public sealed class ListenerInputModel
    {
        [Range(1, 65535)]
        public int HttpsPort { get; set; } = AdminWebListenerConfiguration.DefaultHttpsPort;

        public bool EnableHttp { get; set; }

        [Range(1, 65535)]
        public int HttpPort { get; set; } = AdminWebListenerConfiguration.DefaultHttpPort;
    }
}
