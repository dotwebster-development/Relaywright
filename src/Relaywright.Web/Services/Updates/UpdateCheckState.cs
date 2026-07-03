namespace Relaywright.Web.Services.Updates;

public enum UpdateCheckState
{
    Disabled,
    NeverChecked,
    Checking,
    UpToDate,
    UpdateAvailable,
    CurrentAhead,
    CheckFailed,
    InvalidRelease
}
