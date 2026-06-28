# Windows Deployment Testing

This guide describes the first real-machine deployment lane for the Windows test VM.

## Recommended Shape

Use a GitHub Actions self-hosted runner on the Windows VM. This is safer and simpler than opening WinRM, SSH, or SMB inbound from GitHub-hosted runners. The VM keeps an outbound HTTPS connection to GitHub, downloads the published artifact, and deploys locally.

The workflow in `.github/workflows/deploy-windows-test.yml` does this:

1. Runs on pushes to `development`.
2. Builds and tests on a GitHub-hosted Windows runner.
3. Publishes `src/Relaywright.Web` as self-contained `win-x64`.
4. Uploads the package as an artifact.
5. Runs a deploy job on the Windows VM self-hosted runner with label `relaywright-test-windows`.
6. Installs or updates the `RelaywrightTest` Windows service.
7. Starts the service and checks `/health`.

Self-contained publish means the VM does not need the .NET runtime to run Relaywright. It only needs the GitHub Actions runner and normal Windows service rights.

## VM Preparation

On the Windows VM:

1. Install the GitHub Actions self-hosted runner for this repository.
2. Add the runner labels `Windows` and `relaywright-test-windows`.
3. Install the runner as a Windows service.
4. Run the runner service with administrator rights, preferably the default service setup for a disposable test VM.
5. Allow outbound HTTPS to GitHub.
6. Make sure any VM host/network firewall allows the ports you want to test. The deployment script configures Windows Firewall rules on the guest OS.

The deployment script defaults to:

- install root: `C:\Relaywright\Test`
- data root: `C:\Relaywright\Test\App_Data`
- service name: `RelaywrightTest`
- ASP.NET Core environment: `Production`
- URLs: `https://*:5443;http://*:5080`
- health URL: `https://127.0.0.1:5443/health`
- Windows Firewall admin ports: derived from `RELAYWRIGHT_TEST_URLS`, normally `5443` and `5080`
- Windows Firewall SMTP ports: `2525`

For test VMs, the script can generate a self-signed HTTPS certificate automatically if no certificate variable is configured.

## GitHub Settings

No bootstrap secret is required for the normal Windows test lane. If no admin exists yet, Relaywright shows the first-run setup page.

Optional Actions secret:

- `RELAYWRIGHT_TEST_BOOTSTRAP_PASSWORD`: seeds the initial admin automatically instead of using first-run setup. It must not be `ChangeMe!12345`.

Optional Actions variables:

- `RELAYWRIGHT_TEST_INSTALL_ROOT`
- `RELAYWRIGHT_TEST_SERVICE_NAME`
- `RELAYWRIGHT_TEST_DISPLAY_NAME`
- `RELAYWRIGHT_TEST_ENVIRONMENT`
- `RELAYWRIGHT_TEST_URLS`
- `RELAYWRIGHT_TEST_HEALTH_URL`
- `RELAYWRIGHT_TEST_DATA_DIRECTORY`
- `RELAYWRIGHT_TEST_BOOTSTRAP_USERNAME`
- `RELAYWRIGHT_TEST_BOOTSTRAP_EMAIL`
- `RELAYWRIGHT_TEST_HTTPS_CERTIFICATE_PATH`
- `RELAYWRIGHT_TEST_HTTPS_CERTIFICATE_DNS_NAME`
- `RELAYWRIGHT_TEST_FIREWALL_RULE_PREFIX`
- `RELAYWRIGHT_TEST_FIREWALL_REMOTE_ADDRESS`
- `RELAYWRIGHT_TEST_FIREWALL_PROFILES`
- `RELAYWRIGHT_TEST_FIREWALL_SMTP_PORTS`

Optional Actions secret:

- `RELAYWRIGHT_TEST_HTTPS_CERTIFICATE_PASSWORD`

If you provide a real or pre-created PFX certificate, set both `RELAYWRIGHT_TEST_HTTPS_CERTIFICATE_PATH` and `RELAYWRIGHT_TEST_HTTPS_CERTIFICATE_PASSWORD`. Otherwise the deploy script creates a self-signed test certificate under the install root.

Firewall defaults:

- Rule group/name prefix: `Relaywright Test`
- Remote address: `Any`
- Profiles: `Any`
- SMTP ports: `2525`

To restrict access to your LAN or test client, set `RELAYWRIGHT_TEST_FIREWALL_REMOTE_ADDRESS` to a CIDR or address accepted by Windows Firewall, for example `192.168.1.0/24`.

To test SMTP on port `25`, set:

```text
RELAYWRIGHT_TEST_FIREWALL_SMTP_PORTS=25,2525
```

The deploy script removes and recreates only rules in its own firewall rule group, so repeated deployments stay idempotent.

## First Deploy

From your development machine:

```powershell
git checkout -b development
git add .
git commit -m "Add Windows test deployment lane"
git push -u origin development
```

Then watch GitHub Actions:

- `CI` should run on `development`.
- `Deploy Windows Test` should build, publish, download on the VM, install the service, and pass the health check.

## Windows VM Checks

Run these on the VM after the workflow completes:

```powershell
Get-Service RelaywrightTest
Test-Path C:\Relaywright\Test\App_Data\relay.db
Test-Path C:\Relaywright\Test\App_Data\spool
Test-Path C:\Relaywright\Test\App_Data\keys
Invoke-WebRequest https://127.0.0.1:5443/health -UseBasicParsing
Get-NetFirewallRule -Group "Relaywright Test" | Get-NetFirewallPortFilter
```

If Windows PowerShell rejects the self-signed certificate in `Invoke-WebRequest`, use the browser or temporarily bypass validation for the smoke test:

```powershell
[Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
Invoke-WebRequest https://127.0.0.1:5443/health -UseBasicParsing
```

Open the admin UI:

```text
https://<vm-name-or-ip>:5443/
```

Expected first-login behavior:

- The browser warns about the self-signed certificate unless you configured a trusted certificate.
- If no admin has been created yet, `/Account/Setup` asks for the initial user name and password.
- If you configured `RELAYWRIGHT_TEST_BOOTSTRAP_PASSWORD`, the seeded admin login works with that secret.
- `/health` is anonymous and generic.
- `/health/details` requires authentication.

## SMTP Smoke Test

After login:

1. Change the SMTP listener port to a non-privileged test port such as `2525` unless you intentionally want to test port `25`.
2. Add the test client IP/CIDR as a trusted network.
3. Configure upstream delivery to a safe capture relay, not a real production mailbox.
4. Submit a test message from a trusted machine.

Example PowerShell SMTP submission:

```powershell
$message = [System.Net.Mail.MailMessage]::new(
    "sender@example.test",
    "recipient@example.test",
    "Relaywright Windows smoke test",
    "This is a deployment smoke test.")
$client = [System.Net.Mail.SmtpClient]::new("<vm-name-or-ip>", 2525)
$client.Send($message)
$client.Dispose()
$message.Dispose()
```

Then verify:

- the message appears in the Queue page
- a spool file exists under `C:\Relaywright\Test\App_Data\spool`
- operational events show SMTP session and queue activity
- delivery reaches the expected delivered, retry, failed, or configuration-failure state

## Rollback And Cleanup

The deploy script keeps the latest five releases under:

```text
C:\Relaywright\Test\releases
```

For a disposable test VM, the simplest cleanup is:

```powershell
Stop-Service RelaywrightTest -ErrorAction SilentlyContinue
sc.exe delete RelaywrightTest
Remove-Item C:\Relaywright\Test -Recurse -Force
```

Keep `App_Data` if you are testing upgrade behavior across deployments.
