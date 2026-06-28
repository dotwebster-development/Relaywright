using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Services.Runtime;

namespace Relaywright.Web.Pages.Operations;

public sealed class StatusModel(
    IRuntimeStatusService runtimeStatusService,
    ILogger<StatusModel> logger) : PageModel
{
    public RuntimeStatusSnapshot Status { get; private set; } = new();

    [BindProperty]
    public string? PauseReason { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public IActionResult OnGet()
    {
        return RedirectToPage("/Index");
    }

    public async Task<IActionResult> OnPostPauseAsync(CancellationToken cancellationToken)
    {
        await runtimeStatusService.PauseDeliveryAsync(PauseReason, User.Identity?.Name, cancellationToken);
        StatusMessage = "Outbound delivery paused.";
        logger.LogWarning("Outbound delivery pause requested from operations status page. User={UserName}", User.Identity?.Name);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResumeAsync(CancellationToken cancellationToken)
    {
        await runtimeStatusService.ResumeDeliveryAsync(User.Identity?.Name, cancellationToken);
        StatusMessage = "Outbound delivery resumed.";
        logger.LogInformation("Outbound delivery resume requested from operations status page. User={UserName}", User.Identity?.Name);
        return RedirectToPage();
    }
}
