using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.ConfigurationHistory;

namespace Relaywright.Web.Pages.Operations;

public sealed class ChangeHistoryModel(
    IConfigurationSnapshotService configurationSnapshotService,
    ILogger<ChangeHistoryModel> logger) : PageModel
{
    public IReadOnlyList<ConfigurationSnapshot> Snapshots { get; private set; } = Array.Empty<ConfigurationSnapshot>();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Snapshots = await configurationSnapshotService.GetRecentAsync(100, cancellationToken);
    }

    public async Task<IActionResult> OnPostRollbackAsync(Guid id, CancellationToken cancellationToken)
    {
        await configurationSnapshotService.RollbackAsync(id, User.Identity?.Name, cancellationToken);
        StatusMessage = "Configuration rollback applied.";
        logger.LogWarning(
            "Configuration rollback requested. SnapshotId={SnapshotId}; User={UserName}",
            id,
            User.Identity?.Name);
        return RedirectToPage();
    }
}
