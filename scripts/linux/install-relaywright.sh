#!/usr/bin/env bash
set -Eeuo pipefail

repo="${RELAYWRIGHT_GITHUB_REPOSITORY:-relaywright/relaywright}"
version="1.0.0"
install_root="/opt/relaywright"
data_directory="/var/lib/relaywright"
service_name="relaywright"
display_name="Relaywright"
environment_name="Production"
https_port="5443"
http_port="5080"
enable_http=false
smtp_port="25"
configure_firewall=false
firewall_remote_address="Any"
bootstrap_user_name="admin"
bootstrap_email="admin@localhost"
bootstrap_password="${RELAYWRIGHT_BOOTSTRAP_PASSWORD:-}"
https_certificate_path=""
https_certificate_password=""
generate_self_signed_certificate=true
certificate_dns_name="localhost"
non_interactive=false
uninstall=false
remove_data=false
update=false
health_timeout_seconds=90
sudo_password="${RELAYWRIGHT_SUDO_PASSWORD:-}"

usage() {
    cat <<'USAGE'
Relaywright Linux installer

Usage:
  install-relaywright.sh [options]

Options:
  --version VERSION               Release version to install, for example 1.0.0 or latest.
  --repo OWNER/REPO               GitHub repository that hosts Relaywright releases.
  --install-root PATH             Install root. Default: /opt/relaywright
  --data-directory PATH           Runtime data directory. Default: /var/lib/relaywright
  --service-name NAME             systemd service name. Default: relaywright
  --https-port PORT               Admin HTTPS port. Default: 5443
  --http-port PORT                Admin HTTP port. Use 0 to disable HTTP. Disabled by default.
  --smtp-port PORT                SMTP listener firewall port. Default: 25
  --configure-firewall            Open admin and SMTP TCP ports in firewalld or ufw when active.
  --firewall-remote-address CIDR  Remote address/scope for firewall rules. Default: Any
  --bootstrap-password PASSWORD   Optional first admin bootstrap password.
  --non-interactive               Do not prompt; use supplied flags/defaults.
  --update                        Update an existing installation.
  --uninstall                     Stop and remove the service and installed binaries.
  --remove-data                   With --uninstall, also remove the data directory.
  --help                          Show this help.
USAGE
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version) version="$2"; shift 2 ;;
        --repo) repo="$2"; shift 2 ;;
        --install-root) install_root="$2"; shift 2 ;;
        --data-directory) data_directory="$2"; shift 2 ;;
        --service-name) service_name="$2"; shift 2 ;;
        --display-name) display_name="$2"; shift 2 ;;
        --environment-name) environment_name="$2"; shift 2 ;;
        --https-port) https_port="$2"; shift 2 ;;
        --http-port) http_port="$2"; shift 2 ;;
        --smtp-port) smtp_port="$2"; shift 2 ;;
        --configure-firewall) configure_firewall=true; shift ;;
        --firewall-remote-address) firewall_remote_address="$2"; shift 2 ;;
        --bootstrap-user-name) bootstrap_user_name="$2"; shift 2 ;;
        --bootstrap-email) bootstrap_email="$2"; shift 2 ;;
        --bootstrap-password) bootstrap_password="$2"; shift 2 ;;
        --https-certificate-path) https_certificate_path="$2"; shift 2 ;;
        --https-certificate-password) https_certificate_password="$2"; shift 2 ;;
        --no-self-signed-certificate) generate_self_signed_certificate=false; shift ;;
        --certificate-dns-name) certificate_dns_name="$2"; shift 2 ;;
        --health-timeout-seconds) health_timeout_seconds="$2"; shift 2 ;;
        --non-interactive) non_interactive=true; shift ;;
        --update) update=true; shift ;;
        --uninstall) uninstall=true; shift ;;
        --remove-data) remove_data=true; shift ;;
        --help|-h) usage; exit 0 ;;
        *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
    esac
done

write_step() {
    echo "==> $1"
}

die() {
    echo "ERROR: $1" >&2
    exit 1
}

is_yes() {
    [[ "$1" =~ ^[Yy] ]]
}

prompt_default() {
    local prompt="$1"
    local default="$2"
    if "$non_interactive"; then
        printf '%s' "$default"
        return
    fi

    local value
    read -r -p "$prompt [$default]: " value
    if [[ -z "$value" ]]; then
        printf '%s' "$default"
    else
        printf '%s' "$value"
    fi
}

prompt_yes_no() {
    local prompt="$1"
    local default="$2"
    if "$non_interactive"; then
        "$default" && return 0 || return 1
    fi

    local suffix="y/N"
    "$default" && suffix="Y/n"
    local value
    read -r -p "$prompt [$suffix]: " value
    if [[ -z "$value" ]]; then
        "$default" && return 0 || return 1
    fi

    is_yes "$value"
}

validate_port() {
    local port="$1"
    local name="$2"
    [[ "$port" =~ ^[0-9]+$ ]] || die "$name port '$port' is not a valid TCP port."
    (( port >= 1 && port <= 65535 )) || die "$name port '$port' is not a valid TCP port."
}

run_sudo_with_password() {
    printf '%s\n' "$sudo_password" | sudo -S -p '' "$@"
}

if [[ "${EUID:-$(id -u)}" -eq 0 ]]; then
    SUDO=()
else
    command -v sudo >/dev/null 2>&1 || die "sudo is required when the installer is not run as root."
    if sudo -n true >/dev/null 2>&1; then
        SUDO=(sudo)
    else
        [[ -n "$sudo_password" ]] || die "passwordless sudo is required unless RELAYWRIGHT_SUDO_PASSWORD is configured."
        run_sudo_with_password true >/dev/null 2>&1 || die "configured sudo password was rejected."
        SUDO=(run_sudo_with_password)
    fi
fi

install_dependencies() {
    local missing=()
    for command_name in curl tar openssl sha256sum systemctl; do
        command -v "$command_name" >/dev/null 2>&1 || missing+=("$command_name")
    done

    [[ "${#missing[@]}" -eq 0 ]] && return 0
    write_step "Installing required tools: ${missing[*]}"

    if command -v apt-get >/dev/null 2>&1; then
        "${SUDO[@]}" apt-get update
        "${SUDO[@]}" apt-get install -y curl tar openssl coreutils systemd
        return
    fi

    if command -v dnf >/dev/null 2>&1; then
        "${SUDO[@]}" dnf install -y curl tar openssl coreutils systemd
        return
    fi

    if command -v yum >/dev/null 2>&1; then
        "${SUDO[@]}" yum install -y curl tar openssl coreutils systemd
        return
    fi

    if command -v zypper >/dev/null 2>&1; then
        "${SUDO[@]}" zypper --non-interactive install curl tar openssl coreutils systemd
        return
    fi

    die "Missing required tools (${missing[*]}) and no supported package manager was found."
}

resolve_version() {
    if [[ "$version" != "latest" ]]; then
        version="${version#v}"
        return
    fi

    local effective_url
    effective_url="$(curl -Ls -o /dev/null -w '%{url_effective}' "https://github.com/${repo}/releases/latest")"
    version="${effective_url##*/}"
    version="${version#v}"
    [[ -n "$version" && "$version" != "latest" ]] || die "Could not resolve the latest Relaywright release."
}

write_env_line() {
    local key="$1"
    local value="$2"
    value="${value//\\/\\\\}"
    value="${value//\"/\\\"}"
    value="${value//$'\n'/}"
    printf '%s="%s"\n' "$key" "$value"
}

new_random_password() {
    openssl rand -base64 32
}

generate_certificate() {
    if [[ -z "$https_certificate_path" ]]; then
        https_certificate_path="$install_root/certs/relaywright.pfx"
    fi

    if [[ -f "$https_certificate_path" && -n "$https_certificate_password" ]]; then
        return
    fi

    "$generate_self_signed_certificate" || die "HTTPS requires a certificate. Provide --https-certificate-path and --https-certificate-password, or allow self-signed certificate generation."

    write_step "Creating self-signed HTTPS certificate"
    https_certificate_password="$(new_random_password)"
    local temp_directory
    temp_directory="$(mktemp -d)"
    trap 'rm -rf "$temp_directory"' RETURN

    local certificate_directory
    certificate_directory="$(dirname "$https_certificate_path")"
    "${SUDO[@]}" mkdir -p "$certificate_directory"

    local host_name
    host_name="$(hostname 2>/dev/null || true)"
    local fqdn
    fqdn="$(hostname -f 2>/dev/null || true)"
    local config_path="$temp_directory/openssl.cnf"
    {
        echo "[req]"
        echo "distinguished_name = req_distinguished_name"
        echo "x509_extensions = v3_req"
        echo "prompt = no"
        echo "[req_distinguished_name]"
        echo "CN = $certificate_dns_name"
        echo "[v3_req]"
        echo "subjectAltName = @alt_names"
        echo "[alt_names]"
        echo "DNS.1 = $certificate_dns_name"
        echo "DNS.2 = localhost"
        [[ -n "$host_name" ]] && echo "DNS.3 = $host_name"
        [[ -n "$fqdn" && "$fqdn" != "$host_name" ]] && echo "DNS.4 = $fqdn"
    } > "$config_path"

    openssl req \
        -x509 \
        -nodes \
        -newkey rsa:3072 \
        -days 730 \
        -keyout "$temp_directory/relaywright.key" \
        -out "$temp_directory/relaywright.crt" \
        -config "$config_path" >/dev/null 2>&1

    openssl pkcs12 \
        -export \
        -out "$temp_directory/relaywright.pfx" \
        -inkey "$temp_directory/relaywright.key" \
        -in "$temp_directory/relaywright.crt" \
        -password "pass:$https_certificate_password" >/dev/null 2>&1

    "${SUDO[@]}" install -m 640 "$temp_directory/relaywright.pfx" "$https_certificate_path"
}

get_urls() {
    validate_port "$https_port" "HTTPS"
    local urls="https://*:${https_port}"
    if "$enable_http"; then
        validate_port "$http_port" "HTTP"
        [[ "$http_port" != "$https_port" ]] || die "HTTP and HTTPS ports must be different."
        urls="${urls};http://*:${http_port}"
    fi

    printf '%s' "$urls"
}

get_tcp_ports_from_urls() {
    local url_list="$1"
    printf '%s\n' "$url_list" | tr ';,' '\n' |
        while IFS= read -r url; do
            url="${url//[[:space:]]/}"
            [[ -n "$url" ]] || continue
            if [[ "$url" =~ ^[a-zA-Z][a-zA-Z0-9+.-]*://.*:([0-9]+)(/|$) ]]; then
                echo "${BASH_REMATCH[1]}"
            elif [[ "$url" =~ ^https:// ]]; then
                echo "443"
            elif [[ "$url" =~ ^http:// ]]; then
                echo "80"
            fi
        done | sort -n -u
}

configure_firewall_rules() {
    "$configure_firewall" || return 0

    validate_port "$smtp_port" "SMTP"
    mapfile -t admin_ports < <(get_tcp_ports_from_urls "$urls")
    ports=("${admin_ports[@]}" "$smtp_port")
    write_step "Configuring Linux firewall rules"

    if command -v firewall-cmd >/dev/null 2>&1 && "${SUDO[@]}" firewall-cmd --state >/dev/null 2>&1; then
        for port in "${ports[@]}"; do
            if [[ "$firewall_remote_address" == "Any" || "$firewall_remote_address" == "any" ]]; then
                "${SUDO[@]}" firewall-cmd --permanent --add-port="${port}/tcp" >/dev/null
            else
                "${SUDO[@]}" firewall-cmd --permanent --add-rich-rule="rule family=\"ipv4\" source address=\"$firewall_remote_address\" port protocol=\"tcp\" port=\"$port\" accept" >/dev/null
            fi
            echo "Opened TCP port $port."
        done
        "${SUDO[@]}" firewall-cmd --reload >/dev/null
        return
    fi

    if command -v ufw >/dev/null 2>&1 && "${SUDO[@]}" ufw status | grep -qi "^Status: active"; then
        for port in "${ports[@]}"; do
            if [[ "$firewall_remote_address" == "Any" || "$firewall_remote_address" == "any" ]]; then
                "${SUDO[@]}" ufw allow "${port}/tcp" comment "Relaywright" >/dev/null
            else
                "${SUDO[@]}" ufw allow from "$firewall_remote_address" to any port "$port" proto tcp comment "Relaywright" >/dev/null
            fi
            echo "Opened TCP port $port."
        done
        return
    fi

    echo "No active firewalld or ufw firewall detected; skipping firewall configuration."
}

invoke_health_check() {
    curl --insecure --silent --show-error --fail --max-time 10 "$1"
}

uninstall_relaywright() {
    write_step "Uninstalling Relaywright"
    if "${SUDO[@]}" systemctl list-unit-files "${service_name}.service" >/dev/null 2>&1; then
        "${SUDO[@]}" systemctl stop "$service_name" >/dev/null 2>&1 || true
        "${SUDO[@]}" systemctl disable "$service_name" >/dev/null 2>&1 || true
    fi

    "${SUDO[@]}" rm -f "/etc/systemd/system/${service_name}.service" "/etc/${service_name}.env"
    "${SUDO[@]}" systemctl daemon-reload
    "${SUDO[@]}" rm -rf "$install_root"
    if "$remove_data"; then
        "${SUDO[@]}" rm -rf "$data_directory"
    fi
}

install_dependencies

if ! "$non_interactive" && ! "$uninstall"; then
    version="$(prompt_default "Release version" "$version")"
    install_root="$(prompt_default "Install directory" "$install_root")"
    data_directory="$(prompt_default "Data directory" "$data_directory")"
    https_port="$(prompt_default "Admin HTTPS port" "$https_port")"
    if prompt_yes_no "Enable admin HTTP listener" "$enable_http"; then
        enable_http=true
        http_port="$(prompt_default "Admin HTTP port" "$http_port")"
    else
        enable_http=false
        http_port="0"
    fi
    smtp_port="$(prompt_default "SMTP listener firewall port" "$smtp_port")"
    if prompt_yes_no "Configure firewall" "$configure_firewall"; then
        configure_firewall=true
        firewall_remote_address="$(prompt_default "Firewall remote address" "$firewall_remote_address")"
    else
        configure_firewall=false
    fi
fi

if [[ "$http_port" == "0" ]]; then
    enable_http=false
fi

if "$uninstall"; then
    uninstall_relaywright
    exit 0
fi

resolve_version
urls="$(get_urls)"
health_url="https://127.0.0.1:${https_port}/health"
tag="v${version}"
artifact_name="relaywright-${version}-linux-x64.tar.gz"
base_url="https://github.com/${repo}/releases/download/${tag}"
temp_directory="$(mktemp -d)"
trap 'rm -rf "$temp_directory"' EXIT

write_step "Downloading Relaywright $version from $repo"
curl --fail --location --show-error --output "$temp_directory/$artifact_name" "$base_url/$artifact_name"
curl --fail --location --show-error --output "$temp_directory/SHA256SUMS.txt" "$base_url/SHA256SUMS.txt"

write_step "Verifying checksum"
(
    cd "$temp_directory"
    grep "  ${artifact_name}$" SHA256SUMS.txt > SHA256SUMS.selected
    [[ -s SHA256SUMS.selected ]] || die "Checksum entry for $artifact_name was not found."
    sha256sum -c SHA256SUMS.selected
)

release_name="$version-$(date +%Y%m%d-%H%M%S)"
releases_root="$install_root/releases"
release_path="$releases_root/$release_name"
current_link="$install_root/current"
env_file="/etc/${service_name}.env"
unit_path="/etc/systemd/system/${service_name}.service"

write_step "Preparing directories"
"${SUDO[@]}" mkdir -p "$install_root" "$releases_root" "$data_directory" "$release_path"
"${SUDO[@]}" tar -xzf "$temp_directory/$artifact_name" -C "$release_path"

app_path="$release_path/Relaywright.Web"
[[ -f "$app_path" ]] || die "Relaywright.Web was not found in the release artifact."
"${SUDO[@]}" chmod +x "$app_path"

generate_certificate

if "${SUDO[@]}" systemctl list-unit-files "${service_name}.service" >/dev/null 2>&1; then
    if "${SUDO[@]}" systemctl is-active --quiet "$service_name"; then
        write_step "Stopping service $service_name"
        "${SUDO[@]}" systemctl stop "$service_name"
    fi
fi

write_step "Updating current release link"
"${SUDO[@]}" ln -sfn "$release_path" "$current_link"

write_step "Writing service environment"
env_temp="$(mktemp)"
{
    write_env_line "ASPNETCORE_ENVIRONMENT" "$environment_name"
    write_env_line "ASPNETCORE_URLS" "$urls"
    write_env_line "Storage__DataDirectory" "$data_directory"
    write_env_line "BootstrapAdmin__UserName" "$bootstrap_user_name"
    write_env_line "BootstrapAdmin__Email" "$bootstrap_email"
    [[ -n "$bootstrap_password" ]] && write_env_line "BootstrapAdmin__Password" "$bootstrap_password"
    write_env_line "ASPNETCORE_Kestrel__Certificates__Default__Path" "$https_certificate_path"
    write_env_line "ASPNETCORE_Kestrel__Certificates__Default__Password" "$https_certificate_password"
} > "$env_temp"
"${SUDO[@]}" install -m 600 "$env_temp" "$env_file"
rm -f "$env_temp"

write_step "Writing systemd service"
unit_temp="$(mktemp)"
{
    echo "[Unit]"
    echo "Description=$display_name"
    echo "After=network-online.target"
    echo "Wants=network-online.target"
    echo
    echo "[Service]"
    echo "Type=simple"
    echo "WorkingDirectory=$current_link"
    echo "ExecStart=$current_link/Relaywright.Web"
    echo "EnvironmentFile=$env_file"
    echo "Restart=always"
    echo "RestartSec=10"
    echo "KillSignal=SIGINT"
    echo "SyslogIdentifier=$service_name"
    echo "LimitNOFILE=65535"
    echo
    echo "[Install]"
    echo "WantedBy=multi-user.target"
} > "$unit_temp"
"${SUDO[@]}" install -m 644 "$unit_temp" "$unit_path"
rm -f "$unit_temp"

configure_firewall_rules

write_step "Starting service $service_name"
"${SUDO[@]}" systemctl daemon-reload
"${SUDO[@]}" systemctl enable "$service_name" >/dev/null
"${SUDO[@]}" systemctl restart "$service_name"

write_step "Waiting for health check $health_url"
deadline=$((SECONDS + health_timeout_seconds))
last_error=""
while (( SECONDS < deadline )); do
    if response="$(invoke_health_check "$health_url" 2>&1)"; then
        if grep -Eq '"status"[[:space:]]*:[[:space:]]*"ok"' <<< "$response"; then
            last_error=""
            break
        fi
        last_error="Unexpected health response: $response"
    else
        last_error="$response"
    fi
    sleep 2
done

if [[ -n "$last_error" ]]; then
    "${SUDO[@]}" systemctl status "$service_name" --no-pager || true
    "${SUDO[@]}" journalctl -u "$service_name" -n 80 --no-pager || true
    die "Health check did not pass within ${health_timeout_seconds}s. Last error: $last_error"
fi

write_step "Pruning old releases"
mapfile -t old_releases < <(find "$releases_root" -mindepth 1 -maxdepth 1 -type d -printf '%T@ %p\n' | sort -rn | awk 'NR > 5 {print $2}')
for old_release in "${old_releases[@]}"; do
    [[ "$old_release" == "$releases_root/"* ]] || die "Refusing to remove unexpected old release path '$old_release'."
    "${SUDO[@]}" rm -rf -- "$old_release"
done

write_step "Relaywright installation complete"
echo "Service: $service_name"
echo "Version: $version"
echo "Install root: $install_root"
echo "Data: $data_directory"
echo "Urls: $urls"
