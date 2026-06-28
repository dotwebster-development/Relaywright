#!/usr/bin/env bash
set -Eeuo pipefail

package_path=""
install_root="/opt/relaywright-test"
service_name="relaywright-test"
display_name="Relaywright Test"
environment_name="Production"
urls="https://*:5443;http://*:5080"
health_url="https://127.0.0.1:5443/health"
data_directory="/var/lib/relaywright-test"
bootstrap_user_name="admin"
bootstrap_email="admin@localhost"
bootstrap_password="${RELAYWRIGHT_BOOTSTRAP_PASSWORD:-}"
https_certificate_path=""
https_certificate_password=""
generate_self_signed_certificate=false
certificate_dns_name="localhost"
configure_firewall=false
firewall_remote_address="Any"
firewall_smtp_ports="2525"
health_timeout_seconds=90
sudo_password="${RELAYWRIGHT_LINUX_TEST_SUDO_PASSWORD:-${RELAYWRIGHT_SUDO_PASSWORD:-}}"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --package-path) package_path="$2"; shift 2 ;;
        --install-root) install_root="$2"; shift 2 ;;
        --service-name) service_name="$2"; shift 2 ;;
        --display-name) display_name="$2"; shift 2 ;;
        --environment-name) environment_name="$2"; shift 2 ;;
        --urls) urls="$2"; shift 2 ;;
        --health-url) health_url="$2"; shift 2 ;;
        --data-directory) data_directory="$2"; shift 2 ;;
        --bootstrap-user-name) bootstrap_user_name="$2"; shift 2 ;;
        --bootstrap-email) bootstrap_email="$2"; shift 2 ;;
        --bootstrap-password) bootstrap_password="$2"; shift 2 ;;
        --https-certificate-path) https_certificate_path="$2"; shift 2 ;;
        --https-certificate-password) https_certificate_password="$2"; shift 2 ;;
        --generate-self-signed-certificate) generate_self_signed_certificate=true; shift ;;
        --certificate-dns-name) certificate_dns_name="$2"; shift 2 ;;
        --configure-firewall) configure_firewall=true; shift ;;
        --firewall-remote-address) firewall_remote_address="$2"; shift 2 ;;
        --firewall-smtp-ports) firewall_smtp_ports="$2"; shift 2 ;;
        --health-timeout-seconds) health_timeout_seconds="$2"; shift 2 ;;
        *) echo "Unknown argument: $1" >&2; exit 2 ;;
    esac
done

write_step() {
    echo "==> $1"
}

die() {
    echo "ERROR: $1" >&2
    exit 1
}

require_value() {
    local value="$1"
    local name="$2"
    [[ -n "$value" ]] || die "$name is required."
}

require_value "$package_path" "Package path"

if [[ -z "$data_directory" ]]; then
    data_directory="/var/lib/relaywright-test"
fi

env_file="/etc/${service_name}.env"

escape_env_value() {
    local value="$1"
    value="${value//\\/\\\\}"
    value="${value//\"/\\\"}"
    value="${value//$'\n'/}"
    printf '"%s"' "$value"
}

write_env_line() {
    local key="$1"
    local value="$2"
    printf '%s=%s\n' "$key" "$(escape_env_value "$value")"
}

run_sudo_with_password() {
    printf '%s\n' "$sudo_password" | sudo -S -p '' "$@"
}

if [[ "${EUID:-$(id -u)}" -eq 0 ]]; then
    SUDO=()
else
    command -v sudo >/dev/null 2>&1 || die "sudo is required when the deployment script is not run as root."
    if sudo -n true >/dev/null 2>&1; then
        SUDO=(sudo)
    else
        [[ -n "$sudo_password" ]] || die "passwordless sudo is required for non-interactive deployment unless RELAYWRIGHT_LINUX_TEST_SUDO_PASSWORD is configured."
        run_sudo_with_password true >/dev/null 2>&1 || die "configured sudo password was rejected."
        SUDO=(run_sudo_with_password)
    fi
fi

get_env_file_value() {
    local key="$1"
    [[ -f "$env_file" ]] || return 0

    local line
    line="$(grep -E "^${key}=" "$env_file" | tail -n 1 || true)"
    [[ -n "$line" ]] || return 0

    local value="${line#*=}"
    value="${value%\"}"
    value="${value#\"}"
    value="${value//\\\"/\"}"
    value="${value//\\\\/\\}"
    printf '%s' "$value"
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

get_tcp_ports_from_list() {
    local port_list="$1"
    printf '%s\n' "$port_list" | tr ',;' '\n' | tr '[:space:]' '\n' |
        while IFS= read -r port; do
            [[ -n "$port" ]] || continue
            [[ "$port" =~ ^[0-9]+$ ]] || die "Firewall port '$port' is not a valid TCP port."
            (( port >= 1 && port <= 65535 )) || die "Firewall port '$port' is not a valid TCP port."
            echo "$port"
        done | sort -n -u
}

load_admin_web_listener_settings() {
    local listener_path="$data_directory/admin-web-listener.json"
    [[ -f "$listener_path" ]] || return 0

    command -v python3 >/dev/null 2>&1 || die "python3 is required to read persisted admin web listener settings."

    local output
    output="$(python3 - "$listener_path" <<'PY'
import json
import sys

with open(sys.argv[1], "r", encoding="utf-8") as handle:
    settings = json.load(handle)

https_port = int(settings.get("httpsPort", 5443))
http_port = int(settings.get("httpPort", 5080))
enable_http = bool(settings.get("enableHttp", True))

if https_port < 1 or https_port > 65535:
    raise SystemExit(f"Configured admin HTTPS port '{https_port}' is not valid.")
if enable_http:
    if http_port < 1 or http_port > 65535:
        raise SystemExit(f"Configured admin HTTP port '{http_port}' is not valid.")
    if http_port == https_port:
        raise SystemExit("Configured admin HTTP and HTTPS ports must be different.")

urls = [f"https://*:{https_port}"]
if enable_http:
    urls.append(f"http://*:{http_port}")

print("URLS=" + ";".join(urls))
print(f"HEALTH_URL=https://127.0.0.1:{https_port}/health")
PY
)"

    while IFS='=' read -r key value; do
        case "$key" in
            URLS) urls="$value" ;;
            HEALTH_URL) health_url="$value" ;;
        esac
    done <<< "$output"

    write_step "Using persisted admin web listener settings"
    echo "Admin URLs: $urls"
    echo "Health URL: $health_url"
}

new_random_password() {
    if command -v openssl >/dev/null 2>&1; then
        openssl rand -base64 32
        return
    fi

    head -c 32 /dev/urandom | base64
}

unique_names() {
    awk 'NF && !seen[$0]++'
}

ensure_test_certificate() {
    if [[ -z "$https_certificate_path" ]]; then
        https_certificate_path="$install_root/certs/relaywright-test.pfx"
    fi

    if [[ -z "$https_certificate_password" ]]; then
        https_certificate_password="$(get_env_file_value "ASPNETCORE_Kestrel__Certificates__Default__Password")"
    fi

    if [[ -f "$https_certificate_path" && -n "$https_certificate_password" ]]; then
        return
    fi

    "$generate_self_signed_certificate" || die "Production-like HTTPS requires a certificate. Provide --https-certificate-path and --https-certificate-password, or pass --generate-self-signed-certificate for test VMs."
    command -v openssl >/dev/null 2>&1 || die "openssl is required to generate a self-signed HTTPS certificate."

    write_step "Creating self-signed HTTPS certificate for test VM"
    https_certificate_password="$(new_random_password)"

    local certificate_directory
    certificate_directory="$(dirname "$https_certificate_path")"
    "${SUDO[@]}" mkdir -p "$certificate_directory"

    local temp_directory
    temp_directory="$(mktemp -d)"
    trap 'rm -rf "$temp_directory"' RETURN

    local host_name
    host_name="$(hostname 2>/dev/null || true)"
    local fqdn
    fqdn="$(hostname -f 2>/dev/null || true)"
    mapfile -t dns_names < <(printf '%s\n' "$certificate_dns_name" "$host_name" "$fqdn" "localhost" | unique_names)

    local config_path="$temp_directory/openssl.cnf"
    {
        echo "[req]"
        echo "distinguished_name = req_distinguished_name"
        echo "x509_extensions = v3_req"
        echo "prompt = no"
        echo "[req_distinguished_name]"
        echo "CN = ${dns_names[0]}"
        echo "[v3_req]"
        echo "subjectAltName = @alt_names"
        echo "[alt_names]"
        local dns_index=1
        local ip_index=1
        for name in "${dns_names[@]}"; do
            if [[ "$name" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
                echo "IP.${ip_index} = $name"
                ip_index=$((ip_index + 1))
            else
                echo "DNS.${dns_index} = $name"
                dns_index=$((dns_index + 1))
            fi
        done
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

configure_firewall_rules() {
    "$configure_firewall" || return 0

    mapfile -t admin_ports < <(get_tcp_ports_from_urls "$urls")
    mapfile -t smtp_ports < <(get_tcp_ports_from_list "$firewall_smtp_ports")
    ports=("${admin_ports[@]}" "${smtp_ports[@]}")
    [[ "${#ports[@]}" -gt 0 ]] || return 0

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
        return 0
    fi

    if command -v ufw >/dev/null 2>&1 && "${SUDO[@]}" ufw status | grep -qi "^Status: active"; then
        for port in "${ports[@]}"; do
            if [[ "$firewall_remote_address" == "Any" || "$firewall_remote_address" == "any" ]]; then
                "${SUDO[@]}" ufw allow "${port}/tcp" comment "Relaywright Test" >/dev/null
            else
                "${SUDO[@]}" ufw allow from "$firewall_remote_address" to any port "$port" proto tcp comment "Relaywright Test" >/dev/null
            fi
            echo "Opened TCP port $port."
        done
        return 0
    fi

    echo "No active firewalld or ufw firewall detected; skipping firewall configuration."
}

invoke_health_check() {
    local url="$1"
    [[ -n "$url" ]] || return 0
    curl --insecure --silent --show-error --fail --max-time 10 "$url"
}

write_service_diagnostics() {
    echo "==> Service diagnostics"
    "${SUDO[@]}" systemctl status "$service_name" --no-pager || true
    "${SUDO[@]}" journalctl -u "$service_name" -n 80 --no-pager || true
}

load_admin_web_listener_settings

resolved_package_path="$(realpath "$package_path")"
releases_root="$install_root/releases"
release_name="$(date +%Y%m%d-%H%M%S)"
release_path="$releases_root/$release_name"
staging_root="$install_root/staging"
current_link="$install_root/current"
unit_path="/etc/systemd/system/${service_name}.service"

write_step "Preparing directories"
"${SUDO[@]}" mkdir -p "$install_root" "$releases_root" "$data_directory" "$staging_root"

if [[ -e "$release_path" ]]; then
    [[ "$release_path" == "$releases_root/"* ]] || die "Refusing to remove unexpected release path '$release_path'."
    "${SUDO[@]}" rm -rf -- "$release_path"
fi
"${SUDO[@]}" mkdir -p "$release_path"

if [[ -d "$resolved_package_path" ]]; then
    write_step "Copying package directory to release $release_name"
    "${SUDO[@]}" cp -a "$resolved_package_path"/. "$release_path"/
else
    die "Package path '$package_path' must be an expanded artifact directory."
fi

app_path="$release_path/Relaywright.Web"
[[ -f "$app_path" ]] || die "Relaywright.Web was not found in package path '$package_path'."
"${SUDO[@]}" chmod +x "$app_path"

if [[ "$environment_name" != "Development" && "$urls" == *"https://"* ]]; then
    ensure_test_certificate
fi

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
    if [[ -n "$bootstrap_password" ]]; then
        write_env_line "BootstrapAdmin__Password" "$bootstrap_password"
    fi
    if [[ -n "$https_certificate_path" ]]; then
        write_env_line "ASPNETCORE_Kestrel__Certificates__Default__Path" "$https_certificate_path"
    fi
    if [[ -n "$https_certificate_password" ]]; then
        write_env_line "ASPNETCORE_Kestrel__Certificates__Default__Password" "$https_certificate_password"
    fi
} > "$env_temp"
"${SUDO[@]}" install -m 600 "$env_temp" "$env_file"
rm -f "$env_temp"

write_step "Writing systemd service"
unit_temp="$(mktemp)"
cat > "$unit_temp" <<UNIT
[Unit]
Description=$display_name
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
WorkingDirectory=$current_link
ExecStart=$current_link/Relaywright.Web
EnvironmentFile=$env_file
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=$service_name
LimitNOFILE=65535

[Install]
WantedBy=multi-user.target
UNIT
"${SUDO[@]}" install -m 644 "$unit_temp" "$unit_path"
rm -f "$unit_temp"

configure_firewall_rules

write_step "Starting service $service_name"
"${SUDO[@]}" systemctl daemon-reload
"${SUDO[@]}" systemctl enable "$service_name" >/dev/null
"${SUDO[@]}" systemctl restart "$service_name"

if [[ -n "$health_url" ]]; then
    write_step "Waiting for health check $health_url"
    deadline=$((SECONDS + health_timeout_seconds))
    last_error=""
    while (( SECONDS < deadline )); do
        if response="$(invoke_health_check "$health_url" 2>&1)"; then
            if grep -Eq '"status"[[:space:]]*:[[:space:]]*"ok"' <<< "$response"; then
                write_step "Health check passed"
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
        write_service_diagnostics
        die "Health check did not pass within ${health_timeout_seconds}s. Last error: $last_error"
    fi
fi

write_step "Pruning old releases"
mapfile -t old_releases < <(find "$releases_root" -mindepth 1 -maxdepth 1 -type d -printf '%T@ %p\n' | sort -rn | awk 'NR > 5 {print $2}')
for old_release in "${old_releases[@]}"; do
    [[ "$old_release" == "$releases_root/"* ]] || die "Refusing to remove unexpected old release path '$old_release'."
    "${SUDO[@]}" rm -rf -- "$old_release"
done

write_step "Relaywright Linux test deployment complete"
echo "Service: $service_name"
echo "Release: $release_path"
echo "Data: $data_directory"
echo "Urls: $urls"
