#!/usr/bin/env bash
set -Eeuo pipefail

mode="clean-installer"
version="1.0.0-rc.5"
from_version=""
repository="${GITHUB_REPOSITORY:-relaywright/relaywright}"
github_token="${GITHUB_TOKEN:-}"
artifacts_directory="$PWD/artifacts/linux-release-validation"
sudo_password="${RELAYWRIGHT_LINUX_RELEASE_VALIDATION_SUDO_PASSWORD:-${RELAYWRIGHT_SUDO_PASSWORD:-}}"

installer_service_name="relaywright"
installer_install_root="/opt/relaywright"
installer_data_directory="/var/lib/relaywright"
installer_https_port="5443"
installer_http_port="5080"
installer_smtp_port="25"

update_service_name="relaywright-release-validation"
update_install_root="/opt/relaywright-release-validation"
update_data_directory="/var/lib/relaywright-release-validation"
update_https_port="5543"
update_http_port="5580"
update_smtp_port="2526"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --mode) mode="$2"; shift 2 ;;
        --version) version="$2"; shift 2 ;;
        --from-version) from_version="$2"; shift 2 ;;
        --repository) repository="$2"; shift 2 ;;
        --artifacts-directory) artifacts_directory="$2"; shift 2 ;;
        *) echo "Unknown argument: $1" >&2; exit 2 ;;
    esac
done

case "$mode" in
    clean-installer|update-package|full-release|cleanup-only) ;;
    *) echo "Unknown validation mode: $mode" >&2; exit 2 ;;
esac

mkdir -p "$artifacts_directory"

write_step() {
    echo "==> $1"
}

die() {
    echo "ERROR: $1" >&2
    if declare -F save_diagnostics >/dev/null 2>&1; then
        set +e
        write_artifact failure.txt "ERROR: $1"
        save_diagnostics failure
    fi
    exit 1
}

normalize_version() {
    local value="$1"
    value="${value#v}"
    printf '%s' "$value"
}

release_tag() {
    printf 'v%s' "$(normalize_version "$1")"
}

run_sudo_with_password() {
    printf '%s\n' "$sudo_password" | sudo -S -p '' "$@"
}

if [[ "${EUID:-$(id -u)}" -eq 0 ]]; then
    SUDO=()
else
    command -v sudo >/dev/null 2>&1 || die "sudo is required when validation is not run as root."
    if sudo -n true >/dev/null 2>&1; then
        SUDO=(sudo)
    else
        [[ -n "$sudo_password" ]] || die "passwordless sudo is required unless RELAYWRIGHT_LINUX_RELEASE_VALIDATION_SUDO_PASSWORD is configured."
        run_sudo_with_password true >/dev/null 2>&1 || die "configured sudo password was rejected."
        SUDO=(run_sudo_with_password)
    fi
fi

write_artifact() {
    local name="$1"
    local content="$2"
    printf '%s\n' "$content" > "$artifacts_directory/$name"
}

curl_download() {
    local url="$1"
    local destination="$2"
    local args=(--fail --location --show-error --silent --output "$destination")
    if [[ -n "$github_token" ]]; then
        args+=(--header "Authorization: Bearer $github_token")
    fi

    curl "${args[@]}" "$url"
}

download_release_asset() {
    local release_version="$1"
    local asset_name="$2"
    local destination="$3"
    local tag
    tag="$(release_tag "$release_version")"

    write_step "Downloading $asset_name from $repository $tag"
    curl_download "https://github.com/${repository}/releases/download/${tag}/${asset_name}" "$destination"
}

assert_checksum() {
    local checksums_path="$1"
    local file_path="$2"
    local file_name
    file_name="$(basename "$file_path")"

    (
        cd "$(dirname "$file_path")"
        awk -v asset="$file_name" '$2 == asset || $2 == "*" asset {print}' "$checksums_path" > SHA256SUMS.selected
        [[ -s SHA256SUMS.selected ]] || die "Checksum entry for $file_name was not found."
        sha256sum -c SHA256SUMS.selected
    )
}

download_release_asset_set() {
    local release_version="$1"
    shift
    local version_name
    version_name="$(normalize_version "$release_version")"
    local download_directory="$artifacts_directory/downloads/$version_name"
    mkdir -p "$download_directory"

    local checksums_path="$download_directory/SHA256SUMS.txt"
    download_release_asset "$release_version" "SHA256SUMS.txt" "$checksums_path"

    for asset_name in "$@"; do
        local asset_path="$download_directory/$asset_name"
        download_release_asset "$release_version" "$asset_name" "$asset_path"
        assert_checksum "$checksums_path" "$asset_path"
    done
}

is_any_scope() {
    [[ "${1,,}" == "any" ]]
}

command_available() {
    local command_name="$1"
    if command -v "$command_name" >/dev/null 2>&1; then
        return 0
    fi

    "${SUDO[@]}" sh -c "command -v '$command_name' >/dev/null 2>&1"
}

get_local_ipv4_cidrs() {
    command_available ip || return 0
    "${SUDO[@]}" ip -o -4 addr show scope global up 2>/dev/null |
        awk '{print $4}' |
        sort -u
}

active_firewall_backend() {
    if command_available firewall-cmd && "${SUDO[@]}" firewall-cmd --state >/dev/null 2>&1; then
        echo "firewalld"
        return
    fi

    if command_available ufw && "${SUDO[@]}" ufw status | grep -qi "^Status: active"; then
        echo "ufw"
        return
    fi
}

remove_firewall_rules_for_ports() {
    local ports=("$@")
    local backend
    backend="$(active_firewall_backend || true)"
    [[ -n "$backend" ]] || return 0

    mapfile -t cidrs < <(get_local_ipv4_cidrs)

    if [[ "$backend" == "firewalld" ]]; then
        for port in "${ports[@]}"; do
            "${SUDO[@]}" firewall-cmd --permanent --remove-port="${port}/tcp" >/dev/null 2>&1 || true
            for cidr in "${cidrs[@]}"; do
                "${SUDO[@]}" firewall-cmd --permanent --remove-rich-rule="rule family=\"ipv4\" source address=\"$cidr\" port protocol=\"tcp\" port=\"$port\" accept" >/dev/null 2>&1 || true
            done
        done
        "${SUDO[@]}" firewall-cmd --reload >/dev/null 2>&1 || true
        return
    fi

    if [[ "$backend" == "ufw" ]]; then
        for port in "${ports[@]}"; do
            "${SUDO[@]}" ufw --force delete allow "${port}/tcp" >/dev/null 2>&1 || true
            for cidr in "${cidrs[@]}"; do
                "${SUDO[@]}" ufw --force delete allow from "$cidr" to any port "$port" proto tcp >/dev/null 2>&1 || true
            done
        done
    fi
}

stop_remove_service() {
    local service_name="$1"
    if "${SUDO[@]}" systemctl list-unit-files "${service_name}.service" >/dev/null 2>&1; then
        "${SUDO[@]}" systemctl stop "$service_name" >/dev/null 2>&1 || true
        "${SUDO[@]}" systemctl disable "$service_name" >/dev/null 2>&1 || true
    fi

    "${SUDO[@]}" rm -f "/etc/systemd/system/${service_name}.service" "/etc/${service_name}.env"
    "${SUDO[@]}" systemctl daemon-reload >/dev/null 2>&1 || true
    "${SUDO[@]}" systemctl reset-failed "$service_name" >/dev/null 2>&1 || true
}

known_path() {
    local path="$1"
    case "$path" in
        "$installer_install_root"|"$installer_data_directory"|"$update_install_root"|"$update_data_directory") return 0 ;;
        *) return 1 ;;
    esac
}

remove_known_directory() {
    local path="$1"
    known_path "$path" || die "Refusing to remove unexpected directory: $path"
    if "${SUDO[@]}" test -e "$path"; then
        write_step "Removing directory $path"
        "${SUDO[@]}" rm -rf -- "$path"
    fi
}

invoke_release_validation_cleanup() {
    write_step "Cleaning Relaywright Linux validation state"
    stop_remove_service "$installer_service_name"
    stop_remove_service "$update_service_name"
    remove_firewall_rules_for_ports "$installer_https_port" "$installer_http_port" "$installer_smtp_port" "$update_https_port" "$update_http_port" "$update_smtp_port"
    remove_known_directory "$installer_install_root"
    remove_known_directory "$installer_data_directory"
    remove_known_directory "$update_data_directory"
    remove_known_directory "$update_install_root"
    assert_cleanup_complete
}

assert_cleanup_complete() {
    for service_name in "$installer_service_name" "$update_service_name"; do
        if "${SUDO[@]}" systemctl is-active --quiet "$service_name"; then
            die "Cleanup failed because service $service_name is still active."
        fi
        if "${SUDO[@]}" test -f "/etc/systemd/system/${service_name}.service"; then
            die "Cleanup failed because unit file for $service_name still exists."
        fi
    done

    for path in "$installer_install_root" "$installer_data_directory" "$update_install_root" "$update_data_directory"; do
        if "${SUDO[@]}" test -e "$path"; then
            die "Cleanup failed because $path still exists."
        fi
    done
}

wait_service_running() {
    local service_name="$1"
    local deadline=$((SECONDS + 60))
    while (( SECONDS < deadline )); do
        if "${SUDO[@]}" systemctl is-active --quiet "$service_name"; then
            return
        fi
        sleep 1
    done

    die "Service $service_name did not reach active state."
}

assert_health() {
    local port="$1"
    local output_path="$artifacts_directory/health-${port}.json"
    local deadline=$((SECONDS + 120))
    local response=""
    local last_error=""

    write_step "Waiting for HTTPS health on port $port"
    while (( SECONDS < deadline )); do
        if response="$(curl --insecure --silent --show-error --fail --max-time 10 "https://127.0.0.1:${port}/health" 2>&1)"; then
            printf '%s\n' "$response" > "$output_path"
            if grep -Eq '"status"[[:space:]]*:[[:space:]]*"ok"' <<< "$response"; then
                return
            fi
            last_error="Unexpected health response: $response"
        else
            last_error="$response"
        fi
        sleep 2
    done

    die "Health check failed on port $port. Last error: $last_error"
}

assert_http_disabled() {
    local port="$1"
    if curl --silent --show-error --fail --max-time 5 "http://127.0.0.1:${port}/health" >/dev/null 2>&1; then
        die "HTTP port $port is open, but it should be disabled."
    fi
}

assert_setup_page() {
    local port="$1"
    local output_path="$artifacts_directory/setup-page-${port}.html"
    local response
    response="$(curl --insecure --silent --show-error --fail --max-time 15 "https://127.0.0.1:${port}/Account/Setup")"
    printf '%s\n' "$response" > "$output_path"
    grep -q "Set Up Relaywright" "$output_path" || die "First-run setup page was not reachable on HTTPS port $port."
}

assert_data_layout() {
    local data_directory="$1"
    "${SUDO[@]}" test -f "$data_directory/relay.db" || die "SQLite database was not found in $data_directory."
    "${SUDO[@]}" test -d "$data_directory/spool" || die "Spool directory was not found in $data_directory."
    "${SUDO[@]}" test -d "$data_directory/keys" || die "Data Protection key directory was not found in $data_directory."
    "${SUDO[@]}" test -d "$data_directory/backups" || die "Backup directory was not found in $data_directory."
    "${SUDO[@]}" test -d "$data_directory/certs" || die "Certificate data directory was not found in $data_directory."
}

assert_firewall_rules() {
    local expected_ports_csv="$1"
    local forbidden_ports_csv="$2"
    local backend
    backend="$(active_firewall_backend || true)"
    [[ -n "$backend" ]] || die "No active firewalld or ufw firewall was detected. Release validation requires a managed firewall on the install VM."

    IFS=',' read -r -a expected_ports <<< "$expected_ports_csv"
    IFS=',' read -r -a forbidden_ports <<< "$forbidden_ports_csv"

    if [[ "$backend" == "firewalld" ]]; then
        local ports
        local rich_rules
        ports="$("${SUDO[@]}" firewall-cmd --list-ports)"
        rich_rules="$("${SUDO[@]}" firewall-cmd --list-rich-rules)"
        printf '%s\n\n%s\n' "$ports" "$rich_rules" > "$artifacts_directory/firewall-firewalld.txt"

        for port in "${expected_ports[@]}"; do
            if grep -Eq "(^| )${port}/tcp( |$)" <<< "$ports"; then
                die "Firewall port $port is open to Any; expected scoped local-subnet rule."
            fi
            grep -q "port=\"$port\"" <<< "$rich_rules" || die "Expected firewalld rich rule for TCP port $port was not found."
            grep -q "source address=" <<< "$rich_rules" || die "Expected firewalld source-scoped rules were not found."
        done

        for port in "${forbidden_ports[@]}"; do
            if grep -Eq "(^| )${port}/tcp( |$)" <<< "$ports" || grep -q "port=\"$port\"" <<< "$rich_rules"; then
                die "Forbidden HTTP firewall port $port is present."
            fi
        done
        return
    fi

    local status
    status="$("${SUDO[@]}" ufw status)"
    printf '%s\n' "$status" > "$artifacts_directory/firewall-ufw.txt"
    for port in "${expected_ports[@]}"; do
        grep -q "${port}/tcp" <<< "$status" || die "Expected ufw rule for TCP port $port was not found."
        if grep -E "${port}/tcp[[:space:]]+ALLOW[[:space:]]+Anywhere" <<< "$status" >/dev/null; then
            die "ufw port $port is open to Anywhere; expected scoped local-subnet rule."
        fi
    done

    for port in "${forbidden_ports[@]}"; do
        if grep -q "${port}/tcp" <<< "$status"; then
            die "Forbidden HTTP firewall port $port is present."
        fi
    done
}

run_installer_script() {
    local script_path="$1"
    local install_version="$2"
    local service_name="$3"
    local install_root="$4"
    local data_directory="$5"
    local https_port="$6"
    local http_port="$7"
    local smtp_port="$8"
    local update_flag="$9"
    local firewall_remote_address="${10:-local-subnet}"

    local args=(
        "$script_path"
        --repo "$repository"
        --version "$install_version"
        --service-name "$service_name"
        --display-name "Relaywright Release Validation"
        --install-root "$install_root"
        --data-directory "$data_directory"
        --https-port "$https_port"
        --http-port "$http_port"
        --smtp-port "$smtp_port"
        --health-timeout-seconds 240
        --configure-firewall
        --firewall-remote-address "$firewall_remote_address"
        --non-interactive
    )

    if [[ "$update_flag" == "true" ]]; then
        args+=(--update)
    fi

    "${SUDO[@]}" bash "${args[@]}"
}

download_installer_script() {
    local release_version="$1"
    local version_name
    version_name="$(normalize_version "$release_version")"
    local download_directory="$artifacts_directory/downloads/$version_name"
    mkdir -p "$download_directory"
    local script_path="$download_directory/install-relaywright.sh"

    download_release_asset_set "$release_version" "install-relaywright.sh"
    chmod +x "$script_path"
}

installer_script_path_for() {
    local release_version="$1"
    local version_name
    version_name="$(normalize_version "$release_version")"
    printf '%s/downloads/%s/install-relaywright.sh' "$artifacts_directory" "$version_name"
}

create_update_markers() {
    write_step "Creating update preservation markers"
    "${SUDO[@]}" mkdir -p "$update_data_directory/spool/release-validation" "$update_data_directory/backups"
    printf 'Subject: Relaywright release validation\r\n\r\nPreserve this spool marker across update.\n' |
        "${SUDO[@]}" tee "$update_data_directory/spool/release-validation/preserve.eml" >/dev/null
    printf 'Relaywright release validation backup marker.\n' |
        "${SUDO[@]}" tee "$update_data_directory/backups/release-validation-preserve.txt" >/dev/null
    "${SUDO[@]}" tee "$update_data_directory/admin-web-listener.json" >/dev/null <<JSON
{
  "httpsPort": $update_https_port,
  "enableHttp": false,
  "httpPort": $update_http_port
}
JSON
}

write_update_fingerprint() {
    local name="$1"
    local output_path="$artifacts_directory/$name"
    {
        echo "database=$("${SUDO[@]}" test -f "$update_data_directory/relay.db" && echo present || echo missing)"
        "${SUDO[@]}" sha256sum "$update_data_directory/spool/release-validation/preserve.eml"
        "${SUDO[@]}" sha256sum "$update_data_directory/backups/release-validation-preserve.txt"
        "${SUDO[@]}" sha256sum "$update_data_directory/admin-web-listener.json"
        "${SUDO[@]}" find "$update_data_directory/keys" -type f -name '*.xml' -print0 |
            "${SUDO[@]}" xargs -0 --no-run-if-empty sha256sum |
            sort
    } > "$output_path"
}

assert_update_preserved_data() {
    local before_path="$artifacts_directory/update-fingerprint-before.txt"
    local after_path="$artifacts_directory/update-fingerprint-after.txt"
    cmp -s "$before_path" "$after_path" || die "Update preservation fingerprint changed. See update-fingerprint-before.txt and update-fingerprint-after.txt."
    grep -q '^database=present$' "$after_path" || die "Database was missing after update."
    if ! "${SUDO[@]}" find "$update_data_directory/keys" -type f -name '*.xml' | grep -q .; then
        die "Data Protection keys were not present after update."
    fi
}

validate_installation() {
    local service_name="$1"
    local data_directory="$2"
    local https_port="$3"
    local http_port="$4"
    local smtp_port="$5"
    local validate_firewall="${6:-true}"

    wait_service_running "$service_name"
    assert_health "$https_port"
    assert_http_disabled "$http_port"
    assert_data_layout "$data_directory"
    if [[ "$validate_firewall" == "true" ]]; then
        assert_firewall_rules "${https_port},${smtp_port}" "$http_port"
    fi
}

invoke_clean_installer_validation() {
    local version_name
    version_name="$(normalize_version "$version")"
    download_installer_script "$version"
    local script_path
    script_path="$(installer_script_path_for "$version")"

    invoke_release_validation_cleanup
    write_step "Running clean Linux installer validation for $version_name"
    run_installer_script "$script_path" "$version_name" "$installer_service_name" "$installer_install_root" "$installer_data_directory" "$installer_https_port" "0" "$installer_smtp_port" "false" "local-subnet"
    validate_installation "$installer_service_name" "$installer_data_directory" "$installer_https_port" "$installer_http_port" "$installer_smtp_port"
    assert_setup_page "$installer_https_port"
}

invoke_update_package_validation() {
    [[ -n "$from_version" ]] || die "from_version is required when mode is update-package or full-release."

    local from_version_name
    local to_version_name
    from_version_name="$(normalize_version "$from_version")"
    to_version_name="$(normalize_version "$version")"
    download_installer_script "$from_version"
    download_installer_script "$version"
    local from_script_path
    local to_script_path
    from_script_path="$(installer_script_path_for "$from_version")"
    to_script_path="$(installer_script_path_for "$version")"

    invoke_release_validation_cleanup

    write_step "Installing baseline Linux package $from_version_name"
    run_installer_script "$from_script_path" "$from_version_name" "$update_service_name" "$update_install_root" "$update_data_directory" "$update_https_port" "0" "$update_smtp_port" "false" "Any"
    validate_installation "$update_service_name" "$update_data_directory" "$update_https_port" "$update_http_port" "$update_smtp_port" "false"
    curl --insecure --silent --show-error --fail --max-time 15 "https://127.0.0.1:${update_https_port}/Account/Login" >/dev/null || true

    create_update_markers
    write_update_fingerprint "update-fingerprint-before.txt"

    write_step "Updating Linux package to $to_version_name"
    run_installer_script "$to_script_path" "$to_version_name" "$update_service_name" "$update_install_root" "$update_data_directory" "$update_https_port" "0" "$update_smtp_port" "true" "local-subnet"
    validate_installation "$update_service_name" "$update_data_directory" "$update_https_port" "$update_http_port" "$update_smtp_port"
    write_update_fingerprint "update-fingerprint-after.txt"
    assert_update_preserved_data
}

invoke_full_release_validation() {
    write_step "Running full Linux release validation: update, cleanup, clean installer"
    invoke_update_package_validation
    invoke_release_validation_cleanup
    invoke_clean_installer_validation
}

save_diagnostics() {
    local suffix="$1"
    {
        echo "mode=$mode"
        echo "version=$version"
        echo "from_version=$from_version"
        echo "repository=$repository"
    } > "$artifacts_directory/validation-input-${suffix}.txt"

    for service_name in "$installer_service_name" "$update_service_name"; do
        "${SUDO[@]}" systemctl status "$service_name" --no-pager > "$artifacts_directory/service-${service_name}-${suffix}.txt" 2>&1 || true
        "${SUDO[@]}" journalctl -u "$service_name" -n 120 --no-pager > "$artifacts_directory/journal-${service_name}-${suffix}.txt" 2>&1 || true
    done

    if [[ "$(active_firewall_backend || true)" == "firewalld" ]]; then
        "${SUDO[@]}" firewall-cmd --list-all > "$artifacts_directory/firewall-${suffix}.txt" 2>&1 || true
        "${SUDO[@]}" firewall-cmd --list-rich-rules >> "$artifacts_directory/firewall-${suffix}.txt" 2>&1 || true
    elif [[ "$(active_firewall_backend || true)" == "ufw" ]]; then
        "${SUDO[@]}" ufw status numbered > "$artifacts_directory/firewall-${suffix}.txt" 2>&1 || true
    elif command_available ufw; then
        "${SUDO[@]}" ufw status numbered > "$artifacts_directory/firewall-${suffix}.txt" 2>&1 || true
    else
        printf 'No active firewalld or ufw firewall detected.\n' > "$artifacts_directory/firewall-${suffix}.txt"
    fi
}

succeeded=false
failure_message=""
trap 'failure_message="Validation failed at line $LINENO."; write_artifact failure.txt "$failure_message"; save_diagnostics failure' ERR

write_artifact validation-input.txt "mode=$mode
version=$version
from_version=$from_version
repository=$repository
installer_install_root=$installer_install_root
installer_data_directory=$installer_data_directory
update_install_root=$update_install_root
update_data_directory=$update_data_directory"

case "$mode" in
    clean-installer) invoke_clean_installer_validation ;;
    update-package) invoke_update_package_validation ;;
    full-release) invoke_full_release_validation ;;
    cleanup-only) invoke_release_validation_cleanup ;;
esac

succeeded=true

if [[ "$mode" != "cleanup-only" ]]; then
    invoke_release_validation_cleanup
fi

save_diagnostics final

if "$succeeded"; then
    write_step "Linux release validation completed successfully"
fi
