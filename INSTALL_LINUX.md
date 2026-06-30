# Install Relaywright On Linux

Relaywright for Linux is distributed as self-contained x64 and ARM64 tarballs plus an installer script. A best-effort ARMv7 tarball is also published for older 32-bit ARM hosts until dedicated ARMv7 validation hardware is available. The host does not need the .NET runtime.

## Quick Install

```bash
curl -fsSL https://github.com/relaywright/relaywright/releases/latest/download/install-relaywright.sh \
  | sudo bash -s -- --version latest
```

The script downloads the release tarball, verifies `SHA256SUMS.txt`, installs a systemd service, writes `/etc/relaywright.env`, creates a self-signed HTTPS certificate when needed, and starts Relaywright.

The installer auto-detects the host architecture. Modern Raspberry Pi and similar small-office ARM devices should normally use a 64-bit OS and the `linux-arm64` package. Older 32-bit ARMv7 Linux installs use the best-effort `linux-arm` package.

## Common Options

```bash
sudo bash install-relaywright.sh \
  --version 1.0.0 \
  --runtime auto \
  --install-root /opt/relaywright \
  --data-directory /var/lib/relaywright \
  --service-name relaywright \
  --https-port 5443 \
  --http-port 0 \
  --smtp-port 25 \
  --configure-firewall \
  --firewall-remote-address local-subnet
```

The admin HTTP listener is disabled by default. Use a non-zero `--http-port` only when you intentionally want an HTTP listener.

Use `--runtime linux-x64`, `--runtime linux-arm64`, or `--runtime linux-arm` only when you need to override auto-detection.

Firewall changes are optional on Linux. Pass `--configure-firewall` only when you want the installer to manage host firewall rules. With `--configure-firewall`, the default remote scope is `local-subnet`, which resolves the host's active IPv4 interface CIDRs and creates scoped rules. You can also pass an explicit CIDR such as `192.168.1.0/24`, or `Any` if you intentionally want broad exposure.

The installer supports active `ufw` and `firewalld`. `ufw` is common on Ubuntu, `firewalld` is common on RHEL/Fedora, and some hosts intentionally use unmanaged firewalling. If `--configure-firewall` is requested but neither supported firewall is active, the installer prints a warning and continues.

If the GitHub repository is not `relaywright/relaywright`, pass `--repo OWNER/REPO` or set:

```bash
export RELAYWRIGHT_GITHUB_REPOSITORY=OWNER/REPO
```

## Non-Interactive Install

```bash
sudo bash install-relaywright.sh \
  --version latest \
  --https-port 5443 \
  --http-port 0 \
  --smtp-port 25 \
  --configure-firewall \
  --non-interactive
```

## Update

Run the installer again with `--update` and the target version:

```bash
sudo bash install-relaywright.sh --version latest --update --non-interactive
```

Runtime data is preserved. Releases are installed side-by-side under `/opt/relaywright/releases`, and the latest five releases are retained.

## Uninstall

```bash
sudo bash install-relaywright.sh --uninstall
```

Add `--remove-data` only when you also want to delete the SQLite database, spool, keys, certificates, and backups:

```bash
sudo bash install-relaywright.sh --uninstall --remove-data
```

## After Install

Open `https://localhost:5443` or the HTTPS port you selected. If no bootstrap password was supplied, Relaywright shows the first-run setup page.
