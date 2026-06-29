using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MailKit.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.ConfigurationHistory;
using Relaywright.Web.Services.Relay;

namespace Relaywright.Web.Pages.Settings;

public sealed class RelayModel(
    IRelayConfigurationService relayConfigurationService,
    IConfigurationSnapshotService configurationSnapshotService,
    ILogger<RelayModel> logger) : PageModel
{
    [BindProperty]
    public RelayConfigurationEditModel Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [BindProperty]
    public bool BindToAllInterfaces { get; set; } = true;

    [BindProperty]
    public string? SelectedBindAddress { get; set; }

    public bool ShowAuthenticationPanel => Input.UseUpstreamAuthentication;

    public bool ShowMicrosoftAuthenticationFields =>
        Input.UseUpstreamAuthentication
        && Input.UpstreamAuthenticationMode == UpstreamAuthenticationMode.Microsoft365OAuth;

    public bool ShowBasicAuthenticationFields =>
        Input.UseUpstreamAuthentication
        && Input.UpstreamAuthenticationMode == UpstreamAuthenticationMode.Basic;

    public bool ShowCertificateFields => Input.EnableStartTls;

    public IReadOnlyList<SelectListItem> SecureSocketOptions { get; } =
        Enum.GetValues<SecureSocketOptions>()
            .Select(x => new SelectListItem(x.ToString(), x.ToString()))
            .ToArray();

    public IReadOnlyList<SelectListItem> AuthenticationModes { get; } =
        new[]
        {
            new SelectListItem("Choose type...", string.Empty)
        }
        .Concat(Enum.GetValues<UpstreamAuthenticationMode>()
            .Select(x => new SelectListItem(
                x switch
                {
                    UpstreamAuthenticationMode.Basic => "Basic credentials",
                    UpstreamAuthenticationMode.Microsoft365OAuth => "Microsoft 365 OAuth",
                    _ => x.ToString()
                },
                x.ToString())))
        .ToArray();

    public IReadOnlyList<SelectListItem> BindAddressOptions { get; private set; } = Array.Empty<SelectListItem>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Input = await relayConfigurationService.GetEditModelAsync(cancellationToken);
        InitializeBindSelectionFromModel();

        logger.LogDebug(
            "Relay settings page loaded. Listener={ListenerBindAddress}:{ListenerPort}; UpstreamConfigured={UpstreamConfigured}; AuthMode={AuthMode}; User={UserName}",
            Input.ListenerBindAddress,
            Input.ListenerPort,
            !string.IsNullOrWhiteSpace(Input.UpstreamHost),
            Input.UpstreamAuthenticationMode,
            User.Identity?.Name);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ApplyBindSelection();
        PopulateBindAddressOptions(SelectedBindAddress, BindToAllInterfaces);

        if (!ModelState.IsValid)
        {
            logger.LogWarning(
                "Relay settings save rejected by validation. User={UserName}; ErrorCount={ErrorCount}; BindToAllInterfaces={BindToAllInterfaces}; SelectedBindAddress={SelectedBindAddress}",
                User.Identity?.Name,
                ModelState.ErrorCount,
                BindToAllInterfaces,
                SelectedBindAddress);

            return Page();
        }

        try
        {
            await configurationSnapshotService.CaptureAsync(
                ConfigurationSnapshotService.RelayArea,
                User.Identity?.Name,
                "Snapshot before relay settings save.",
                cancellationToken);
            await relayConfigurationService.SaveAsync(Input, cancellationToken);
            StatusMessage = "Relay settings saved.";
            logger.LogInformation(
                "Relay settings save completed. User={UserName}; Listener={ListenerBindAddress}:{ListenerPort}; UpstreamConfigured={UpstreamConfigured}; AuthMode={AuthMode}; BindToAllInterfaces={BindToAllInterfaces}",
                User.Identity?.Name,
                Input.ListenerBindAddress,
                Input.ListenerPort,
                !string.IsNullOrWhiteSpace(Input.UpstreamHost),
                Input.UpstreamAuthenticationMode,
                BindToAllInterfaces);

            return RedirectToPage();
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Relay settings save failed. User={UserName}; Listener={ListenerBindAddress}:{ListenerPort}; UpstreamConfigured={UpstreamConfigured}; AuthMode={AuthMode}",
                User.Identity?.Name,
                Input.ListenerBindAddress,
                Input.ListenerPort,
                !string.IsNullOrWhiteSpace(Input.UpstreamHost),
                Input.UpstreamAuthenticationMode);

            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    private void InitializeBindSelectionFromModel()
    {
        var bindToAllInterfaces = IsWildcardBindAddress(Input.ListenerBindAddress);
        var selectedBindAddress = bindToAllInterfaces ? null : Input.ListenerBindAddress;
        PopulateBindAddressOptions(selectedBindAddress, bindToAllInterfaces);
    }

    private void ApplyBindSelection()
    {
        if (BindToAllInterfaces)
        {
            Input.ListenerBindAddress = IPAddress.Any.ToString();
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedBindAddress))
        {
            ModelState.AddModelError(nameof(SelectedBindAddress), "Select a local IP address to bind the SMTP listener.");
            return;
        }

        Input.ListenerBindAddress = SelectedBindAddress.Trim();
    }

    private void PopulateBindAddressOptions(string? selectedBindAddress, bool bindToAllInterfaces)
    {
        var options = GetBindAddressOptions().ToList();

        if (!bindToAllInterfaces
            && !string.IsNullOrWhiteSpace(selectedBindAddress)
            && options.All(x => !string.Equals(x.Value, selectedBindAddress, StringComparison.OrdinalIgnoreCase)))
        {
            options.Insert(0, new SelectListItem($"{selectedBindAddress} (configured)", selectedBindAddress));
        }

        BindAddressOptions = options;
        BindToAllInterfaces = bindToAllInterfaces;
        SelectedBindAddress = bindToAllInterfaces
            ? options.FirstOrDefault()?.Value
            : selectedBindAddress ?? options.FirstOrDefault()?.Value;
    }

    private static bool IsWildcardBindAddress(string? address)
    {
        return string.IsNullOrWhiteSpace(address)
            || string.Equals(address, "0.0.0.0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(address, "::", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<SelectListItem> GetBindAddressOptions()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var options = new List<SelectListItem>();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(x => x.OperationalStatus == OperationalStatus.Up))
        {
            foreach (var unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
            {
                var address = unicastAddress.Address;
                if (address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
                {
                    continue;
                }

                if (address.IsIPv6LinkLocal)
                {
                    continue;
                }

                var text = address.ToString();
                if (!seen.Add(text))
                {
                    continue;
                }

                options.Add(new SelectListItem($"{text} ({networkInterface.Name})", text));
            }
        }

        options.Sort((left, right) => string.Compare(left.Value, right.Value, StringComparison.OrdinalIgnoreCase));
        return options;
    }
}
