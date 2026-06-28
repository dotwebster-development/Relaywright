# Linux Deployment Testing

This guide describes the Linux test VM deployment lane.

## Recommended Shape

Use a GitHub Actions self-hosted runner on the Linux VM. The runner keeps an outbound HTTPS connection to GitHub, downloads the published Linux artifact, and deploys locally with systemd.

The workflow in `.github/workflows/deploy-linux-test.yml` does this:

1. Runs on pushes to `development`.
2. Builds and tests on a GitHub-hosted Ubuntu runner.
3. Publishes `src/Relaywright.Web` as self-contained `linux-x64`.
4. Uploads the package as an artifact.
5. Runs a deploy job on a Linux self-hosted runner.
6. Installs or updates the `relaywright-test` systemd service.
7. Starts the service and checks `/health`.

Self-contained publish means the VM does not need the .NET runtime to run Relaywright.

## VM Preparation

On the Linux VM:

1. Install the GitHub Actions self-hosted runner.
2. Make sure it has the default labels `self-hosted`, `Linux`, and `X64`.
3. Give the runner non-interactive administrative rights. Preferred: passwordless `sudo` for the runner account. Alternative: set the `RELAYWRIGHT_LINUX_TEST_SUDO_PASSWORD` Actions secret. Disposable test VM option: run the runner service as root.
4. Make sure `systemd`, `curl`, and `openssl` are available.
5. Allow outbound HTTPS to GitHub.
6. Make sure any VM host/network firewall allows the ports you want to test. The deployment script can add rules for active `firewalld` or `ufw`.

Default deployment values:

- install root: `/opt/relaywright-test`
- data root: `/var/lib/relaywright-test`
- service name: `relaywright-test`
- ASP.NET Core environment: `Production`
- URLs: `https://*:5443;http://*:5080`
- health URL: `https://127.0.0.1:5443/health`
- SMTP firewall ports: `2525`

For test VMs, the script can generate a self-signed HTTPS certificate automatically if no certificate variable is configured.

If you later change the admin web listener from `Settings -> Web Interface`, Relaywright stores the desired HTTPS/HTTP ports in `/var/lib/relaywright-test/admin-web-listener.json` by default. On the next deployment, the script uses that persisted listener configuration for the service URLs, firewall ports, and health check URL.

## GitHub Settings

No bootstrap secret is required for the normal Linux test lane. If no admin exists yet, Relaywright shows the first-run setup page.

Optional Actions variable:

- `RELAYWRIGHT_LINUX_TEST_RUNS_ON`: JSON array for runner labels, for example `["self-hosted","Linux","X64","relaywright-test-linux"]`.

Optional Actions variables:

- `RELAYWRIGHT_LINUX_TEST_INSTALL_ROOT`
- `RELAYWRIGHT_LINUX_TEST_SERVICE_NAME`
- `RELAYWRIGHT_LINUX_TEST_DISPLAY_NAME`
- `RELAYWRIGHT_LINUX_TEST_ENVIRONMENT`
- `RELAYWRIGHT_LINUX_TEST_URLS`
- `RELAYWRIGHT_LINUX_TEST_HEALTH_URL`
- `RELAYWRIGHT_LINUX_TEST_DATA_DIRECTORY`
- `RELAYWRIGHT_LINUX_TEST_BOOTSTRAP_USERNAME`
- `RELAYWRIGHT_LINUX_TEST_BOOTSTRAP_EMAIL`
- `RELAYWRIGHT_LINUX_TEST_HTTPS_CERTIFICATE_PATH`
- `RELAYWRIGHT_LINUX_TEST_HTTPS_CERTIFICATE_DNS_NAME`
- `RELAYWRIGHT_LINUX_TEST_FIREWALL_REMOTE_ADDRESS`
- `RELAYWRIGHT_LINUX_TEST_FIREWALL_SMTP_PORTS`

Optional Actions secrets:

- `RELAYWRIGHT_LINUX_TEST_BOOTSTRAP_PASSWORD`
- `RELAYWRIGHT_LINUX_TEST_HTTPS_CERTIFICATE_PASSWORD`
- `RELAYWRIGHT_LINUX_TEST_SUDO_PASSWORD`

If you provide a real or pre-created PFX certificate, set both `RELAYWRIGHT_LINUX_TEST_HTTPS_CERTIFICATE_PATH` and `RELAYWRIGHT_LINUX_TEST_HTTPS_CERTIFICATE_PASSWORD`. Otherwise the deploy script creates a self-signed test certificate under the install root.

## Linux VM Checks

Run these on the VM after the workflow completes:

```bash
systemctl status relaywright-test --no-pager
test -f /var/lib/relaywright-test/relay.db
test -d /var/lib/relaywright-test/spool
test -d /var/lib/relaywright-test/keys
curl --insecure https://127.0.0.1:5443/health
```

Open the admin UI:

```text
https://<vm-name-or-ip>:5443/
```

Expected first-login behavior:

- The browser warns about the self-signed certificate unless you configured a trusted certificate.
- If no admin has been created yet, `/Account/Setup` asks for the initial user name and password.
- `/health` is anonymous and generic.
- `/health/details` requires authentication.
