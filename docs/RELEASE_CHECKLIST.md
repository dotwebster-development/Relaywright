# Release Checklist

Use this checklist before publishing a stable Relaywright release.

## Local Gates

Run from the repository root:

```powershell
dotnet restore Relaywright.sln
dotnet build Relaywright.sln
dotnet test tests/Relaywright.Web.Tests/Relaywright.Web.Tests.csproj
dotnet build Relaywright.sln --configuration Release --no-restore
dotnet test tests/Relaywright.Web.Tests/Relaywright.Web.Tests.csproj --configuration Release --no-build
dotnet list Relaywright.sln package --vulnerable --include-transitive
dotnet format Relaywright.sln --verify-no-changes --no-restore
```

The vulnerability report must not contain any project with vulnerable packages.

## Windows Validation

- Run the `Validate Windows Release` workflow in `clean-installer` mode against the release candidate on `DOT-WINDOWS02`.
- Run the same workflow in `update-package` mode when a published baseline artifact is available for upgrade testing.
- Clean install the Windows installer on a disposable VM.
- Confirm first-run setup works without a bootstrap password.
- Confirm HTTPS is enabled and the admin HTTP listener is disabled unless explicitly selected.
- Confirm Windows Firewall rules do not expose admin ports beyond the selected scope.
- Configure trusted and untrusted SMTP clients, then verify accepted and denied submissions.
- Upgrade an existing `0.1.0-beta.1` data directory to `1.0.0` and verify configuration, trusted networks, queue metadata, spool files, Data Protection keys, certificates, backups, and admin login state.

## Linux Validation

- Clean install with `install-relaywright.sh --version <version>`.
- Confirm the systemd service starts and `/health` returns `ok`.
- Confirm HTTPS is enabled and HTTP is disabled unless a non-zero `--http-port` is supplied.
- Upgrade an existing `0.1.0-beta.1` data directory with `--update`.
- Verify firewall behavior on active `firewalld` and `ufw` hosts when `--configure-firewall` is used.

## Soak And Failure Checks

- Run 24 to 72 hours of synthetic SMTP traffic through a local capture relay.
- Include accepted, denied, retried, expired, delivered, paused, resumed, and restarted flows.
- Test disk-full or spool-unwritable behavior, DB lock contention, upstream outage, bad certificate password, app restart during active delivery, and backup/restore validation.

Record the tested commit, artifacts, OS versions, installer options, and any deviations before publishing the GitHub release.
