using Relaywright.Web.Infrastructure;
using Relaywright.Web.Options;

namespace Relaywright.Web.Services.Updates;

public sealed record UpdateCheckStatus(
    UpdateCheckState State,
    string CurrentVersion,
    string Repository,
    string? LatestVersion = null,
    string? ReleaseName = null,
    string? ReleaseUrl = null,
    DateTimeOffset? ReleasePublishedUtc = null,
    DateTimeOffset? LastCheckedUtc = null,
    DateTimeOffset? NextCheckUtc = null,
    string? Message = null)
{
    public bool IsUpdateAvailable => State == UpdateCheckState.UpdateAvailable;

    public string Label => State switch
    {
        UpdateCheckState.Disabled => "Disabled",
        UpdateCheckState.NeverChecked => "Not checked",
        UpdateCheckState.Checking => "Checking",
        UpdateCheckState.UpToDate => "Current",
        UpdateCheckState.UpdateAvailable => "Update available",
        UpdateCheckState.CurrentAhead => "Ahead",
        UpdateCheckState.CheckFailed => "Check failed",
        UpdateCheckState.InvalidRelease => "Invalid release",
        _ => "Unknown"
    };

    public string BadgeClass => State switch
    {
        UpdateCheckState.UpToDate or UpdateCheckState.CurrentAhead => "status-enabled",
        UpdateCheckState.UpdateAvailable => "severity-warning",
        UpdateCheckState.CheckFailed or UpdateCheckState.InvalidRelease => "status-failed",
        UpdateCheckState.Disabled => "status-disabled",
        _ => "status-unknown"
    };

    public static UpdateCheckStatus Initial()
    {
        return NeverChecked(ApplicationVersion.DisplayVersion, UpdateCheckOptions.DefaultRepository);
    }

    public static UpdateCheckStatus NeverChecked(string currentVersion, string repository)
    {
        return new UpdateCheckStatus(
            UpdateCheckState.NeverChecked,
            currentVersion,
            repository,
            Message: "Release check has not run yet.");
    }

    public static UpdateCheckStatus Disabled(string currentVersion, string repository)
    {
        return new UpdateCheckStatus(
            UpdateCheckState.Disabled,
            currentVersion,
            repository,
            Message: "Release checks are disabled.");
    }
}
