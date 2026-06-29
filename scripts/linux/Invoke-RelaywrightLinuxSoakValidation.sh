#!/usr/bin/env bash
set -Eeuo pipefail

mode="soak"
version="1.0.0-rc.7"
repository="${GITHUB_REPOSITORY:-relaywright/relaywright}"
github_token="${GITHUB_TOKEN:-}"
artifacts_directory="$PWD/artifacts/linux-soak-validation"
duration_minutes="10"
message_rate_per_minute="10"
restart_interval_minutes="5"
upstream_outage_seconds="15"
cleanup_after="true"
sudo_password="${RELAYWRIGHT_LINUX_SOAK_VALIDATION_SUDO_PASSWORD:-${RELAYWRIGHT_LINUX_RELEASE_VALIDATION_SUDO_PASSWORD:-${RELAYWRIGHT_SUDO_PASSWORD:-}}}"

service_name="relaywright-soak"
install_root="/opt/relaywright-soak"
data_directory="/var/lib/relaywright-soak"
https_port="5643"
http_probe_port="5080"
relay_smtp_port="2527"
capture_host="127.0.0.1"
capture_port="2528"

capture_pid=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --mode) mode="$2"; shift 2 ;;
        --version) version="$2"; shift 2 ;;
        --repository) repository="$2"; shift 2 ;;
        --artifacts-directory) artifacts_directory="$2"; shift 2 ;;
        --duration-minutes) duration_minutes="$2"; shift 2 ;;
        --message-rate-per-minute) message_rate_per_minute="$2"; shift 2 ;;
        --restart-interval-minutes) restart_interval_minutes="$2"; shift 2 ;;
        --upstream-outage-seconds) upstream_outage_seconds="$2"; shift 2 ;;
        --cleanup-after) cleanup_after="$2"; shift 2 ;;
        *) echo "Unknown argument: $1" >&2; exit 2 ;;
    esac
done

case "$mode" in
    soak|cleanup-only) ;;
    *) echo "Unknown validation mode: $mode" >&2; exit 2 ;;
esac

mkdir -p "$artifacts_directory"

write_step() {
    echo "==> $1"
}

write_artifact() {
    local name="$1"
    local content="$2"
    printf '%s\n' "$content" > "$artifacts_directory/$name"
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

is_true() {
    [[ "${1,,}" == "true" || "$1" == "1" || "${1,,}" == "yes" ]]
}

validate_positive_integer() {
    local value="$1"
    local name="$2"
    [[ "$value" =~ ^[0-9]+$ ]] || die "$name must be a non-negative integer."
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
        [[ -n "$sudo_password" ]] || die "passwordless sudo is required unless a Relaywright sudo password secret is configured."
        run_sudo_with_password true >/dev/null 2>&1 || die "configured sudo password was rejected."
        SUDO=(run_sudo_with_password)
    fi
fi

command_available() {
    local command_name="$1"
    if command -v "$command_name" >/dev/null 2>&1; then
        return 0
    fi

    "${SUDO[@]}" sh -c "command -v '$command_name' >/dev/null 2>&1"
}

install_dependencies() {
    local missing=()
    for command_name in curl sha256sum systemctl openssl python3 sqlite3; do
        command_available "$command_name" || missing+=("$command_name")
    done

    [[ "${#missing[@]}" -eq 0 ]] && return 0
    write_step "Installing required soak tools: ${missing[*]}"

    if command_available apt-get; then
        "${SUDO[@]}" apt-get update
        "${SUDO[@]}" apt-get install -y curl coreutils systemd openssl python3 sqlite3
        return
    fi

    if command_available dnf; then
        "${SUDO[@]}" dnf install -y curl coreutils systemd openssl python3 sqlite
        return
    fi

    if command_available yum; then
        "${SUDO[@]}" yum install -y curl coreutils systemd openssl python3 sqlite
        return
    fi

    die "Missing required soak tools (${missing[*]}) and no supported package manager was found."
}

reset_soak_artifacts() {
    rm -rf \
        "$artifacts_directory/captured" \
        "$artifacts_directory/transcripts" \
        "$artifacts_directory/downloads" \
        "$artifacts_directory/capture_smtp.py" \
        "$artifacts_directory/send_smtp.py" \
        "$artifacts_directory/capture-server.log"
    rm -f \
        "$artifacts_directory"/failure.txt \
        "$artifacts_directory"/health*.json \
        "$artifacts_directory"/journal-*.txt \
        "$artifacts_directory"/queue-counts-*.txt \
        "$artifacts_directory"/queue-status-raw-*.txt \
        "$artifacts_directory"/service-*.txt \
        "$artifacts_directory"/traffic-summary.txt \
        "$artifacts_directory"/validation-input-final.txt \
        "$artifacts_directory"/validation-input-failure.txt \
        "$artifacts_directory"/validation-input-cleanup.txt
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

download_installer_script() {
    local version_name
    version_name="$(normalize_version "$version")"
    local tag
    tag="$(release_tag "$version")"
    local download_directory="$artifacts_directory/downloads/$version_name"
    mkdir -p "$download_directory"

    local checksums_path="$download_directory/SHA256SUMS.txt"
    local script_path="$download_directory/install-relaywright.sh"
    local base_url="https://github.com/${repository}/releases/download/${tag}"

    write_step "Downloading Relaywright installer script for $tag"
    curl_download "$base_url/SHA256SUMS.txt" "$checksums_path"
    curl_download "$base_url/install-relaywright.sh" "$script_path"
    (
        cd "$download_directory"
        awk '$2 == "install-relaywright.sh" || $2 == "*install-relaywright.sh" {print}' SHA256SUMS.txt > SHA256SUMS.selected
        [[ -s SHA256SUMS.selected ]] || die "Checksum entry for install-relaywright.sh was not found."
        sha256sum -c SHA256SUMS.selected
    )
    chmod +x "$script_path"
}

installer_script_path() {
    printf '%s/downloads/%s/install-relaywright.sh' "$artifacts_directory" "$(normalize_version "$version")"
}

known_path() {
    local path="$1"
    case "$path" in
        "$install_root"|"$data_directory") return 0 ;;
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

stop_remove_service() {
    local name="$1"
    if "${SUDO[@]}" systemctl list-unit-files "${name}.service" >/dev/null 2>&1; then
        "${SUDO[@]}" systemctl stop "$name" >/dev/null 2>&1 || true
        "${SUDO[@]}" systemctl disable "$name" >/dev/null 2>&1 || true
    fi

    "${SUDO[@]}" rm -f "/etc/systemd/system/${name}.service" "/etc/${name}.env"
    "${SUDO[@]}" systemctl daemon-reload >/dev/null 2>&1 || true
    "${SUDO[@]}" systemctl reset-failed "$name" >/dev/null 2>&1 || true
}

invoke_cleanup() {
    write_step "Cleaning Relaywright Linux soak state"
    stop_capture_server
    stop_remove_service "$service_name"
    remove_known_directory "$install_root"
    remove_known_directory "$data_directory"
    assert_cleanup_complete
}

assert_cleanup_complete() {
    if "${SUDO[@]}" systemctl is-active --quiet "$service_name"; then
        die "Cleanup failed because service $service_name is still active."
    fi

    if "${SUDO[@]}" test -f "/etc/systemd/system/${service_name}.service"; then
        die "Cleanup failed because unit file for $service_name still exists."
    fi

    for path in "$install_root" "$data_directory"; do
        if "${SUDO[@]}" test -e "$path"; then
            die "Cleanup failed because $path still exists."
        fi
    done
}

wait_service_running() {
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
    local output_path="$artifacts_directory/health.json"
    local deadline=$((SECONDS + 120))
    local response=""
    local last_error=""

    while (( SECONDS < deadline )); do
        if response="$(curl --insecure --silent --show-error --fail --max-time 10 "https://127.0.0.1:${https_port}/health" 2>&1)"; then
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

    die "Health check failed. Last error: $last_error"
}

assert_http_disabled() {
    if curl --silent --show-error --fail --max-time 5 "http://127.0.0.1:${http_probe_port}/health" >/dev/null 2>&1; then
        die "HTTP probe port $http_probe_port is open, but admin HTTP should be disabled."
    fi
}

run_installer() {
    local script_path
    script_path="$(installer_script_path)"
    local version_name
    version_name="$(normalize_version "$version")"

    write_step "Installing Relaywright $version_name for soak validation"
    "${SUDO[@]}" bash "$script_path" \
        --repo "$repository" \
        --version "$version_name" \
        --service-name "$service_name" \
        --display-name "Relaywright Soak Validation" \
        --install-root "$install_root" \
        --data-directory "$data_directory" \
        --https-port "$https_port" \
        --http-port 0 \
        --smtp-port "$relay_smtp_port" \
        --non-interactive

    wait_service_running
    assert_health
    assert_http_disabled
}

configure_relay_for_soak() {
    local database_path="$data_directory/relay.db"
    local now
    now="$(date -u +"%Y-%m-%dT%H:%M:%S+00:00")"

    write_step "Configuring Relaywright relay settings for local soak"
    "${SUDO[@]}" systemctl stop "$service_name"
    "${SUDO[@]}" sqlite3 "$database_path" <<SQL
UPDATE "RelayConfigurations"
SET
    "ListenerBindAddress" = '127.0.0.1',
    "ListenerPort" = $relay_smtp_port,
    "ListenerHostName" = 'relaywright-soak',
    "UpstreamHost" = '$capture_host',
    "UpstreamPort" = $capture_port,
    "UpstreamSecureSocketOptions" = 0,
    "UseUpstreamAuthentication" = 0,
    "UpstreamAuthenticationMode" = 0,
    "UpstreamUserName" = NULL,
    "ProtectedUpstreamPassword" = NULL,
    "DeliveryConcurrency" = 2,
    "MaxRetryCount" = 5,
    "InitialRetryDelaySeconds" = 2,
    "MaxRetryDelaySeconds" = 5,
    "MessageExpirationHours" = 2,
    "DeliveredRetentionHours" = 24,
    "FailedRetentionHours" = 24,
    "UpdatedUtc" = '$now'
WHERE "Id" = 1;
SQL
    "${SUDO[@]}" systemctl start "$service_name"
    wait_service_running
    assert_health
}

write_capture_server() {
    local script_path="$artifacts_directory/capture_smtp.py"
    cat > "$script_path" <<'PY'
import argparse
import os
import pathlib
import socketserver
import threading
import time

class CaptureStore:
    def __init__(self, output):
        self.output = pathlib.Path(output)
        self.output.mkdir(parents=True, exist_ok=True)
        self.lock = threading.Lock()
        self.count = 0

    def save(self, data):
        with self.lock:
            self.count += 1
            path = self.output / f"message-{time.time_ns()}-{os.getpid()}-{self.count:08d}.eml"
        path.write_bytes(data)

class Handler(socketserver.StreamRequestHandler):
    def write_line(self, value):
        self.wfile.write(value.encode("ascii") + b"\r\n")
        self.wfile.flush()

    def handle(self):
        self.write_line("220 relaywright-soak-capture ESMTP ready")
        data_mode = False
        data = []
        while True:
            line = self.rfile.readline(65536)
            if not line:
                return
            stripped = line.rstrip(b"\r\n")
            if data_mode:
                if stripped == b".":
                    self.server.store.save(b"".join(data))
                    data.clear()
                    data_mode = False
                    self.write_line("250 Ok")
                else:
                    data.append(line)
                continue

            command = stripped.decode("utf-8", "replace")
            verb = command.split(" ", 1)[0].upper()
            if verb in ("EHLO", "HELO"):
                self.write_line("250-relaywright-soak-capture")
                self.write_line("250 SIZE 10485760")
            elif verb == "MAIL":
                self.write_line("250 Ok")
            elif verb == "RCPT":
                self.write_line("250 Ok")
            elif verb == "DATA":
                data_mode = True
                self.write_line("354 End data with <CRLF>.<CRLF>")
            elif verb == "RSET":
                data.clear()
                data_mode = False
                self.write_line("250 Ok")
            elif verb == "NOOP":
                self.write_line("250 Ok")
            elif verb == "QUIT":
                self.write_line("221 Bye")
                return
            else:
                self.write_line("250 Ok")

class Server(socketserver.ThreadingMixIn, socketserver.TCPServer):
    allow_reuse_address = True
    daemon_threads = True

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", required=True)
    parser.add_argument("--port", required=True, type=int)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    with Server((args.host, args.port), Handler) as server:
        server.store = CaptureStore(args.output)
        server.serve_forever(poll_interval=0.5)

if __name__ == "__main__":
    main()
PY
}

write_smtp_client() {
    local script_path="$artifacts_directory/send_smtp.py"
    cat > "$script_path" <<'PY'
import argparse
import socket
import sys
from pathlib import Path

def read_response(file, transcript):
    lines = []
    while True:
        line = file.readline()
        if not line:
            raise RuntimeError("connection closed while waiting for SMTP response")
        text = line.decode("utf-8", "replace").rstrip("\r\n")
        lines.append(text)
        transcript.append(f"S: {text}")
        if len(text) >= 4 and text[:3].isdigit() and text[3] == " ":
            return int(text[:3]), lines

def send_command(file, transcript, command):
    transcript.append(f"C: {command}")
    file.write(command.encode("ascii") + b"\r\n")
    file.flush()
    return read_response(file, transcript)

def require(code, expected, step):
    if code not in expected:
        raise RuntimeError(f"{step} returned SMTP {code}, expected {expected}")

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", required=True)
    parser.add_argument("--port", required=True, type=int)
    parser.add_argument("--subject", required=True)
    parser.add_argument("--body", required=True)
    parser.add_argument("--transcript", required=True)
    args = parser.parse_args()

    transcript = []
    try:
        with socket.create_connection((args.host, args.port), timeout=10) as sock:
            sock.settimeout(10)
            file = sock.makefile("rwb", buffering=0)
            code, _ = read_response(file, transcript)
            require(code, {220}, "greeting")
            code, _ = send_command(file, transcript, "EHLO relaywright-soak.local")
            require(code, {250}, "EHLO")
            code, _ = send_command(file, transcript, "MAIL FROM:<soak-sender@example.test>")
            require(code, {250}, "MAIL FROM")
            code, _ = send_command(file, transcript, "RCPT TO:<soak-recipient@example.test>")
            require(code, {250}, "RCPT TO")
            code, _ = send_command(file, transcript, "DATA")
            require(code, {354}, "DATA")
            message = (
                f"Subject: {args.subject}\r\n"
                "From: soak-sender@example.test\r\n"
                "To: soak-recipient@example.test\r\n"
                "\r\n"
                f"{args.body}\r\n"
                ".\r\n"
            )
            transcript.append("C: <message body>")
            file.write(message.encode("utf-8"))
            file.flush()
            code, _ = read_response(file, transcript)
            require(code, {250}, "message body")
            try:
                send_command(file, transcript, "QUIT")
            except Exception:
                pass
    except Exception as exc:
        transcript.append(f"ERROR: {exc}")
        Path(args.transcript).write_text("\n".join(transcript) + "\n", encoding="utf-8")
        return 1

    Path(args.transcript).write_text("\n".join(transcript) + "\n", encoding="utf-8")
    return 0

if __name__ == "__main__":
    sys.exit(main())
PY
}

start_capture_server() {
    write_capture_server
    mkdir -p "$artifacts_directory/captured"
    write_step "Starting local capture SMTP server on $capture_host:$capture_port"
    python3 "$artifacts_directory/capture_smtp.py" \
        --host "$capture_host" \
        --port "$capture_port" \
        --output "$artifacts_directory/captured" \
        > "$artifacts_directory/capture-server.log" 2>&1 &
    capture_pid="$!"
    wait_capture_server
}

stop_capture_server() {
    if [[ -n "${capture_pid:-}" ]] && kill -0 "$capture_pid" >/dev/null 2>&1; then
        kill "$capture_pid" >/dev/null 2>&1 || true
        wait "$capture_pid" >/dev/null 2>&1 || true
    fi
    capture_pid=""
}

wait_capture_server() {
    local deadline=$((SECONDS + 30))
    while (( SECONDS < deadline )); do
        if python3 - "$capture_host" "$capture_port" <<'PY'
import socket
import sys
host = sys.argv[1]
port = int(sys.argv[2])
try:
    with socket.create_connection((host, port), timeout=2):
        pass
except OSError:
    sys.exit(1)
PY
        then
            return
        fi
        sleep 1
    done

    die "Capture SMTP server did not start."
}

send_message() {
    local sequence="$1"
    local kind="$2"
    local transcript="$artifacts_directory/transcripts/${sequence}-${kind}.txt"
    mkdir -p "$artifacts_directory/transcripts"
    python3 "$artifacts_directory/send_smtp.py" \
        --host "127.0.0.1" \
        --port "$relay_smtp_port" \
        --subject "Relaywright soak $kind $sequence" \
        --body "Relaywright soak validation message $sequence ($kind)." \
        --transcript "$transcript"
}

count_captured_messages() {
    find "$artifacts_directory/captured" -type f -name '*.eml' 2>/dev/null | wc -l | tr -d ' '
}

sqlite_scalar() {
    local sql="$1"
    "${SUDO[@]}" sqlite3 "$data_directory/relay.db" "$sql" | tr -d '\r'
}

queue_count_for_status() {
    local status="$1"
    sqlite_scalar "SELECT COUNT(*) FROM \"QueuedMessages\" WHERE \"Status\" = $status;"
}

write_queue_counts() {
    local name="$1"
    local pending
    local in_progress
    local retry_scheduled
    local delivered
    local failed
    local expired
    local delivery_attempts
    local spool_files
    local captured

    pending="$(queue_count_for_status 0)"
    in_progress="$(queue_count_for_status 1)"
    retry_scheduled="$(queue_count_for_status 2)"
    delivered="$(queue_count_for_status 3)"
    failed="$(queue_count_for_status 4)"
    expired="$(queue_count_for_status 5)"
    delivery_attempts="$(sqlite_scalar 'SELECT COUNT(*) FROM "DeliveryAttempts";')"
    spool_files="$("${SUDO[@]}" find "$data_directory/spool" -type f -name '*.eml' 2>/dev/null | wc -l | tr -d ' ')"
    captured="$(count_captured_messages)"

    {
        echo "pending=$pending"
        echo "in_progress=$in_progress"
        echo "retry_scheduled=$retry_scheduled"
        echo "delivered=$delivered"
        echo "failed=$failed"
        echo "expired=$expired"
        echo "delivery_attempts=$delivery_attempts"
        echo "spool_files=$spool_files"
        echo "captured=$captured"
    } > "$artifacts_directory/$name"
}

restart_relaywright() {
    write_step "Restarting $service_name during soak"
    "${SUDO[@]}" systemctl restart "$service_name"
    wait_service_running
    assert_health
}

run_traffic() {
    validate_positive_integer "$duration_minutes" "duration_minutes"
    validate_positive_integer "$message_rate_per_minute" "message_rate_per_minute"
    validate_positive_integer "$restart_interval_minutes" "restart_interval_minutes"
    validate_positive_integer "$upstream_outage_seconds" "upstream_outage_seconds"
    (( duration_minutes > 0 )) || die "duration_minutes must be greater than zero."
    (( message_rate_per_minute > 0 )) || die "message_rate_per_minute must be greater than zero."

    write_smtp_client

    local duration_seconds=$((duration_minutes * 60))
    local start_seconds=$SECONDS
    local deadline=$((start_seconds + duration_seconds))
    local next_restart=0
    local outage_at=$((start_seconds + (duration_seconds / 3)))
    local outage_done=false
    local sleep_interval
    sleep_interval="$(awk -v rate="$message_rate_per_minute" 'BEGIN { printf "%.3f", 60 / rate }')"
    if (( restart_interval_minutes > 0 )); then
        next_restart=$((start_seconds + restart_interval_minutes * 60))
    fi

    local sequence=0
    local accepted=0
    write_step "Sending soak traffic for ${duration_minutes} minute(s) at ${message_rate_per_minute}/minute"
    while (( SECONDS < deadline )); do
        if (( next_restart > 0 && SECONDS >= next_restart )); then
            restart_relaywright
            next_restart=$((SECONDS + restart_interval_minutes * 60))
        fi

        if [[ "$outage_done" == "false" ]] && (( upstream_outage_seconds > 0 && SECONDS >= outage_at )); then
            sequence=$((sequence + 1))
            write_step "Simulating upstream outage for ${upstream_outage_seconds}s"
            stop_capture_server
            if send_message "$sequence" "upstream-outage"; then
                accepted=$((accepted + 1))
            else
                die "Relaywright did not accept message during upstream outage. See transcript $sequence-upstream-outage.txt."
            fi
            sleep "$upstream_outage_seconds"
            start_capture_server
            outage_done=true
        fi

        sequence=$((sequence + 1))
        if send_message "$sequence" "normal"; then
            accepted=$((accepted + 1))
        else
            die "Relaywright did not accept normal soak message $sequence."
        fi

        sleep "$sleep_interval"
    done

    write_artifact traffic-summary.txt "attempted=$sequence
accepted=$accepted
duration_minutes=$duration_minutes
message_rate_per_minute=$message_rate_per_minute
restart_interval_minutes=$restart_interval_minutes
upstream_outage_seconds=$upstream_outage_seconds"
    write_queue_counts "queue-counts-after-traffic.txt"
}

accepted_message_count() {
    awk -F= '$1 == "accepted" {print $2}' "$artifacts_directory/traffic-summary.txt"
}

wait_for_queue_to_drain() {
    local accepted
    accepted="$(accepted_message_count)"
    local deadline=$((SECONDS + 180))
    write_step "Waiting for $accepted accepted message(s) to deliver to capture server"

    while (( SECONDS < deadline )); do
        local pending
        local in_progress
        local retry_scheduled
        local delivered
        local failed
        local expired
        local captured
        pending="$(queue_count_for_status 0)"
        in_progress="$(queue_count_for_status 1)"
        retry_scheduled="$(queue_count_for_status 2)"
        delivered="$(queue_count_for_status 3)"
        failed="$(queue_count_for_status 4)"
        expired="$(queue_count_for_status 5)"
        captured="$(count_captured_messages)"

        if (( delivered >= accepted && captured >= accepted && pending == 0 && in_progress == 0 && retry_scheduled == 0 && failed == 0 && expired == 0 )); then
            write_queue_counts "queue-counts-final.txt"
            return
        fi
        sleep 2
    done

    write_queue_counts "queue-counts-final.txt"
    die "Queue did not drain cleanly before timeout."
}

save_diagnostics() {
    local suffix="$1"
    {
        echo "mode=$mode"
        echo "version=$version"
        echo "repository=$repository"
        echo "duration_minutes=$duration_minutes"
        echo "message_rate_per_minute=$message_rate_per_minute"
        echo "restart_interval_minutes=$restart_interval_minutes"
        echo "upstream_outage_seconds=$upstream_outage_seconds"
        echo "service_name=$service_name"
        echo "install_root=$install_root"
        echo "data_directory=$data_directory"
        echo "https_port=$https_port"
        echo "relay_smtp_port=$relay_smtp_port"
        echo "capture_port=$capture_port"
    } > "$artifacts_directory/validation-input-${suffix}.txt"

    "${SUDO[@]}" systemctl status "$service_name" --no-pager > "$artifacts_directory/service-${suffix}.txt" 2>&1 || true
    "${SUDO[@]}" journalctl -u "$service_name" -n 300 --no-pager > "$artifacts_directory/journal-${suffix}.txt" 2>&1 || true
    curl --insecure --silent --show-error --max-time 10 "https://127.0.0.1:${https_port}/health" > "$artifacts_directory/health-${suffix}.json" 2>&1 || true

    if "${SUDO[@]}" test -f "$data_directory/relay.db"; then
        write_queue_counts "queue-counts-${suffix}.txt" || true
        "${SUDO[@]}" sqlite3 "$data_directory/relay.db" \
            'SELECT "Status", COUNT(*) FROM "QueuedMessages" GROUP BY "Status" ORDER BY "Status";' \
            > "$artifacts_directory/queue-status-raw-${suffix}.txt" 2>&1 || true
    fi
}

invoke_soak() {
    install_dependencies
    reset_soak_artifacts
    invoke_cleanup
    download_installer_script
    run_installer
    configure_relay_for_soak
    start_capture_server
    run_traffic
    wait_for_queue_to_drain
    assert_health
    assert_http_disabled
    save_diagnostics final
    write_step "Linux soak validation completed successfully"
}

trap 'stop_capture_server' EXIT
trap 'write_artifact failure.txt "Soak validation failed at line $LINENO."; save_diagnostics failure' ERR

write_artifact validation-input.txt "mode=$mode
version=$version
repository=$repository
duration_minutes=$duration_minutes
message_rate_per_minute=$message_rate_per_minute
restart_interval_minutes=$restart_interval_minutes
upstream_outage_seconds=$upstream_outage_seconds
cleanup_after=$cleanup_after"

case "$mode" in
    cleanup-only)
        install_dependencies
        invoke_cleanup
        save_diagnostics final
        ;;
    soak)
        invoke_soak
        if is_true "$cleanup_after"; then
            invoke_cleanup
            save_diagnostics cleanup
        fi
        ;;
esac
