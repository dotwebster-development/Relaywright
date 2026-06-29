using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Backups;
using Relaywright.Web.Services.ConfigurationHistory;

namespace Relaywright.Web.Pages.Operations;

public sealed class BackupsModel(
    IBackupService backupService,
    IConfigurationSnapshotService configurationSnapshotService,
    ILogger<BackupsModel> logger) : PageModel
{
    public IReadOnlyList<BackupRun> Runs { get; private set; } = Array.Empty<BackupRun>();

    public BackupReadiness Readiness { get; private set; } = new();

    [BindProperty]
    public BackupScheduleState Schedule { get; set; } = new();

    [BindProperty]
    public string? EncryptionPassword { get; set; }

    [BindProperty]
    public string? ValidationPassword { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        var run = await backupService.CreateBackupAsync(
            User.Identity?.Name,
            scheduled: false,
            cancellationToken,
            EncryptionPassword);
        StatusMessage = run.Status == BackupRunStatus.Succeeded
            ? run.IsEncrypted ? "Encrypted backup created." : "Backup created."
            : $"Backup failed: {run.Message}";
        logger.LogInformation(
            "Manual backup requested. BackupId={BackupId}; Status={Status}; User={UserName}",
            run.Id,
            run.Status,
            User.Identity?.Name);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveScheduleAsync(CancellationToken cancellationToken)
    {
        await configurationSnapshotService.CaptureAsync(
            ConfigurationSnapshotService.BackupScheduleArea,
            User.Identity?.Name,
            "Snapshot before backup schedule save.",
            cancellationToken);
        await backupService.SaveScheduleAsync(Schedule, cancellationToken);
        StatusMessage = "Backup schedule saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostValidateAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await backupService.ValidateAsync(id, cancellationToken, ValidationPassword);
        StatusMessage = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await backupService.DeleteAsync(id, cancellationToken);
        StatusMessage = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetDownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var path = await backupService.GetBackupPathAsync(id, cancellationToken);
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            return NotFound();
        }

        var contentType = string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase)
            ? "application/zip"
            : "application/octet-stream";
        return PhysicalFile(path, contentType, Path.GetFileName(path));
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Runs = await backupService.GetRunsAsync(cancellationToken);
        Schedule = await backupService.GetScheduleAsync(cancellationToken);
        Readiness = await backupService.GetReadinessAsync(cancellationToken);
    }
}
