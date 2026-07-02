using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Backups;
using Relaywright.Web.Services.ConfigurationHistory;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.UI;

namespace Relaywright.Web.Pages.Operations;

public sealed class BackupsModel(
    IBackupService backupService,
    IConfigurationSnapshotService configurationSnapshotService,
    IBackupRestoreService backupRestoreService,
    IApplicationRestartService applicationRestartService,
    ILogger<BackupsModel> logger) : PageModel
{
    public IReadOnlyList<BackupRun> Runs { get; private set; } = Array.Empty<BackupRun>();

    public BackupReadiness Readiness { get; private set; } = new();

    public BackupRestoreSummary? RestoreSummary { get; private set; }

    public BackupScheduleVisibility ScheduleVisibility { get; private set; } = BackupScheduleVisibility.Empty;

    public DateTimeOffset LoadedUtc { get; private set; }

    [BindProperty]
    public BackupScheduleState Schedule { get; set; } = new();

    [BindProperty]
    public string? EncryptionPassword { get; set; }

    [BindProperty]
    public string? ValidationPassword { get; set; }

    [BindProperty]
    public IFormFile? RestoreBackupFile { get; set; }

    [BindProperty]
    public string? RestoreEncryptionPassword { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? RestoreSummaryJson { get; set; }

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
            ? run.IsEncrypted ? "Encrypted backup file created in Backup History." : "Backup file created in Backup History."
            : $"Backup failed: {run.Message}";
        RestoreSummaryJson = null;
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

    public async Task<IActionResult> OnPostStageRestoreAsync(CancellationToken cancellationToken)
    {
        if (RestoreBackupFile is null || RestoreBackupFile.Length <= 0)
        {
            StatusMessage = "Select a Relaywright backup file.";
            return RedirectToPage();
        }

        var restore = await backupRestoreService.StageRestoreAsync(
            RestoreBackupFile,
            RestoreEncryptionPassword,
            cancellationToken);

        if (!restore.Succeeded)
        {
            StatusMessage = restore.Message;
            RestoreSummaryJson = null;
            logger.LogWarning(
                "Backup restore staging failed from admin page. FileName={FileName}; User={UserName}; Message={Message}",
                RestoreBackupFile.FileName,
                User.Identity?.Name,
                restore.Message);
            return RedirectToPage();
        }

        var restart = await applicationRestartService.RequestRestartAsync(
            "Backup restore staged.",
            User.Identity?.Name,
            cancellationToken);

        StatusMessage = $"{restore.Message} {restart.Message}";
        RestoreSummaryJson = restore.Summary is null ? null : JsonSerializer.Serialize(restore.Summary);
        logger.LogWarning(
            "Backup restore staged from admin page. FileName={FileName}; User={UserName}; RestartScheduled={RestartScheduled}",
            RestoreBackupFile.FileName,
            User.Identity?.Name,
            restart.RestartScheduled);
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
        LoadedUtc = DateTimeOffset.UtcNow;
        Runs = await backupService.GetRunsAsync(cancellationToken);
        Schedule = await backupService.GetScheduleAsync(cancellationToken);
        Readiness = await backupService.GetReadinessAsync(cancellationToken);
        ScheduleVisibility = BuildScheduleVisibility(Schedule, Runs, Readiness, LoadedUtc);
        RestoreSummary = string.IsNullOrWhiteSpace(RestoreSummaryJson)
            ? null
            : JsonSerializer.Deserialize<BackupRestoreSummary>(RestoreSummaryJson);
    }

    public static BackupScheduleVisibility BuildScheduleVisibility(
        BackupScheduleState schedule,
        IReadOnlyList<BackupRun> runs,
        BackupReadiness readiness,
        DateTimeOffset loadedUtc)
    {
        var successfulRuns = runs
            .Where(x => x.Status == BackupRunStatus.Succeeded)
            .OrderByDescending(x => x.StartedUtc)
            .ToList();
        var retentionCount = Math.Max(0, schedule.RetentionCount);
        var nextRunUtc = schedule.IsEnabled && schedule.LastRunUtc is not null
            ? schedule.LastRunUtc.Value.AddHours(schedule.IntervalHours)
            : (DateTimeOffset?)null;
        var latestValidated = successfulRuns
            .Where(x => x.LastValidationSucceeded == true)
            .OrderByDescending(x => x.LastValidatedUtc ?? x.StartedUtc)
            .FirstOrDefault();
        var validationAge = readiness.LastGoodBackupAgeHours is null
            ? "Not available"
            : $"{readiness.LastGoodBackupAgeHours.Value:N0} hour(s)";
        var message = schedule.IsEnabled
            ? nextRunUtc is null
                ? "Waiting for first scheduled run."
                : nextRunUtc <= loadedUtc
                    ? "Next scheduled backup is due."
                    : $"Next scheduled backup {TimeFormatter.FormatRelative(nextRunUtc.Value, loadedUtc)}."
            : "Scheduled backups are disabled.";

        return new BackupScheduleVisibility(
            schedule.IsEnabled,
            schedule.IntervalHours,
            retentionCount,
            schedule.LastRunUtc,
            nextRunUtc,
            latestValidated?.LastValidatedUtc ?? latestValidated?.StartedUtc,
            validationAge,
            successfulRuns.Count,
            Math.Min(successfulRuns.Count, retentionCount),
            Math.Max(0, successfulRuns.Count - retentionCount),
            message);
    }
}

public sealed record BackupScheduleVisibility(
    bool IsEnabled,
    int IntervalHours,
    int RetentionCount,
    DateTimeOffset? LastRunUtc,
    DateTimeOffset? NextRunUtc,
    DateTimeOffset? LastValidatedUtc,
    string ValidationAge,
    int SuccessfulBackupCount,
    int RetainedBackupCount,
    int PrunableSucceededBackupCount,
    string Message)
{
    public static BackupScheduleVisibility Empty { get; } = new(
        false,
        0,
        0,
        null,
        null,
        null,
        "Not available",
        0,
        0,
        0,
        "Schedule not loaded.");
}
