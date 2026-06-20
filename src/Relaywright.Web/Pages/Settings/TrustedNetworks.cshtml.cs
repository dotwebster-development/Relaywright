using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Pages.Settings;

public sealed class TrustedNetworksModel(
    ITrustedNetworkService trustedNetworkService,
    ILogger<TrustedNetworksModel> logger) : PageModel
{
    [BindProperty]
    public TrustedNetwork Input { get; set; } = new();

    public IReadOnlyList<TrustedNetwork> Networks { get; private set; } = Array.Empty<TrustedNetwork>();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(int? editId, CancellationToken cancellationToken)
    {
        Networks = await trustedNetworkService.GetAllAsync(cancellationToken);
        Input = editId is null
            ? new TrustedNetwork { IsEnabled = true }
            : Networks.FirstOrDefault(x => x.Id == editId.Value) ?? new TrustedNetwork { IsEnabled = true };

        logger.LogDebug(
            "Trusted networks page loaded. EditId={EditId}; NetworkCount={NetworkCount}; User={UserName}",
            editId,
            Networks.Count,
            User.Identity?.Name);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await trustedNetworkService.AddOrUpdateAsync(Input, cancellationToken);
            StatusMessage = "Trusted network saved.";
            logger.LogInformation(
                "Trusted network save completed from admin page. Id={TrustedNetworkId}; Cidr={Cidr}; Description={Description}; Enabled={Enabled}; User={UserName}",
                Input.Id,
                Input.Cidr,
                Input.Description,
                Input.IsEnabled,
                User.Identity?.Name);

            return RedirectToPage();
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Trusted network save failed from admin page. Id={TrustedNetworkId}; Cidr={Cidr}; Description={Description}; Enabled={Enabled}; User={UserName}",
                Input.Id,
                Input.Cidr,
                Input.Description,
                Input.IsEnabled,
                User.Identity?.Name);

            Networks = await trustedNetworkService.GetAllAsync(cancellationToken);
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        await trustedNetworkService.DeleteAsync(id, cancellationToken);
        StatusMessage = "Trusted network deleted.";
        logger.LogInformation("Trusted network delete requested from admin page. Id={TrustedNetworkId}; User={UserName}", id, User.Identity?.Name);
        return RedirectToPage();
    }
}
