#!/usr/bin/env bash
set -Eeuo pipefail

mode="ugly-paths"
version="1.0.0-rc.7"
repository="${GITHUB_REPOSITORY:-relaywright/relaywright}"
github_token="${GITHUB_TOKEN:-}"
artifacts_directory="$PWD/artifacts/linux-ugly-path-validation"
cleanup_after="true"
sudo_password="${RELAYWRIGHT_LINUX_UGLY_VALIDATION_SUDO_PASSWORD:-${RELAYWRIGHT_LINUX_RELEASE_VALIDATION_SUDO_PASSWORD:-${RELAYWRIGHT_SUDO_PASSWORD:-}}}"

service_name="relaywright-ugly"
install_root="/opt/relaywright-ugly"
data_directory="/var/lib/relaywright-ugly"
https_port="5743"
http_probe_port="5080"
relay_smtp_port="2529"
capture_host="127.0.0.1"
capture_port="2530"

capture_pid=""
db_lock_pid=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --mode) mode="$2"; shift 2 ;;
        --version) version="$2"; shift 2 ;;
        --repository) repository="$2"; shift 2 ;;
        --artifacts-directory) artifacts_directory="$2"; shift 2 ;;
        --cleanup-after) cleanup_after="$2"; shift 2 ;;
        *) echo "Unknown argument: $1" >&2; exit 2 ;;
    esac
done

case "$mode" in
    ugly-paths|cleanup-only) ;;
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

append_result() {
    local name="$1"
    local result="$2"
    printf '%s=%s\n' "$name" "$result" >> "$artifacts_directory/results.txt"
}

die() {
    echo "ERROR: $1" >&2
    write_artifact failure.txt "ERROR: $1"
    if declare -F save_diagnostics >/dev/null 2>&1; then
        set +e
        stop_capture_server
        stop_db_lock
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
    for command_name in curl sha256sum systemctl openssl python3 sqlite3 tar timeout; do
        command_available "$command_name" || missing+=("$command_name")
    done

    [[ "${#missing[@]}" -eq 0 ]] && return 0
    write_step "Installing required ugly-path tools: ${missing[*]}"

    if command_available apt-get; then
        "${SUDO[@]}" apt-get update
        "${SUDO[@]}" apt-get install -y curl coreutils systemd openssl python3 sqlite3 tar
        return
    fi

    if command_available dnf; then
        "${SUDO[@]}" dnf install -y curl coreutils systemd openssl python3 sqlite tar
        return
    fi

    if command_available yum; then
        "${SUDO[@]}" yum install -y curl coreutils systemd openssl python3 sqlite tar
        return
    fi

    die "Missing required ugly-path tools (${missing[*]}) and no supported package manager was found."
}

reset_artifacts() {
    rm -rf \
        "$artifacts_directory/captured-restart" \
        "$artifacts_directory/captured-upstream" \
        "$artifacts_directory/downloads" \
        "$artifacts_directory/transcripts"
    rm -f \
        "$artifacts_directory"/capture-server-*.log \
        "$artifacts_directory"/capture_smtp.py \
        "$artifacts_directory"/db-lock.py \
        "$artifacts_directory"/failure.txt \
        "$artifacts_directory"/health-*.json \
        "$artifacts_directory"/health-*.txt \
        "$artifacts_directory"/http-disabled.txt \
        "$artifacts_directory"/journal-*.txt \
        "$artifacts_directory"/queue-counts-*.txt \
        "$artifacts_directory"/queue-status-raw-*.txt \
        "$artifacts_directory"/results.txt \
        "$artifacts_directory"/send_smtp.py \
        "$artifacts_directory"/service-*.txt \
        "$artifacts_directory"/validation-input-*.txt
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

stop_capture_server() {
    if [[ -n "${capture_pid:-}" ]] && kill -0 "$capture_pid" >/dev/null 2>&1; then
        kill "$capture_pid" >/dev/null 2>&1 || true
        wait "$capture_pid" >/dev/null 2>&1 || true
    fi
    capture_pid=""
}

stop_db_lock() {
    if [[ -n "${db_lock_pid:-}" ]] && kill -0 "$db_lock_pid" >/dev/null 2>&1; then
        kill "$db_lock_pid" >/dev/null 2>&1 || true
        wait "$db_lock_pid" >/dev/null 2>&1 || true
    fi
    db_lock_pid=""
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
    write_step "Cleaning Relaywright Linux ugly-path state"
    stop_capture_server
    stop_db_lock
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
    local deadline=$((SECONDS + 90))
    while (( SECONDS < deadline )); do
        if "${SUDO[@]}" systemctl is-active --quiet "$service_name"; then
            return
        fi
        sleep 1
    done

    die "Service $service_name did not reach active state."
}

health_request() {
    local suffix="$1"
    local output_path="$artifacts_directory/health-${suffix}.json"
    local status_path="$artifacts_directory/health-${suffix}.txt"
    local status_code

    set +e
    status_code="$(curl --insecure --silent --show-error --max-time 10 --output "$output_path" --write-out "%{http_code}" "https://127.0.0.1:${https_port}/health" 2>"$status_path.err")"
    local curl_status=$?
    set -e

    {
        echo "curl_status=$curl_status"
        echo "http_status=$status_code"
        if [[ -s "$status_path.err" ]]; then
            echo "curl_error=$(cat "$status_path.err")"
        fi
    } > "$status_path"
    rm -f "$status_path.err"

    printf '%s:%s' "$curl_status" "$status_code"
}

assert_health_ok() {
    local suffix="$1"
    local result
    result="$(health_request "$suffix")"
    local curl_status="${result%%:*}"
    local http_status="${result##*:}"

    [[ "$curl_status" == "0" ]] || die "Health check $suffix failed with curl status $curl_status."
    [[ "$http_status" == "200" ]] || die "Health check $suffix returned HTTP $http_status, expected 200."
    grep -Eq '"status"[[:space:]]*:[[:space:]]*"ok"' "$artifacts_directory/health-${suffix}.json" || die "Health check $suffix did not return ok."
}

assert_health_not_ok() {
    local suffix="$1"
    local result
    result="$(health_request "$suffix")"
    local curl_status="${result%%:*}"
    local http_status="${result##*:}"

    if [[ "$curl_status" == "0" && "$http_status" == "200" ]] && grep -Eq '"status"[[:space:]]*:[[:space:]]*"ok"' "$artifacts_directory/health-${suffix}.json"; then
        die "Health check $suffix returned ok, but an ugly-path failure was expected."
    fi
}

assert_http_disabled() {
    if curl --silent --show-error --fail --max-time 5 "http://127.0.0.1:${http_probe_port}/health" > "$artifacts_directory/http-disabled.txt" 2>&1; then
        die "HTTP probe port $http_probe_port is open, but admin HTTP should be disabled."
    fi
}

run_installer() {
    local script_path
    script_path="$(installer_script_path)"
    local version_name
    version_name="$(normalize_version "$version")"

    write_step "Installing Relaywright $version_name for ugly-path validation"
    "${SUDO[@]}" bash "$script_path" \
        --repo "$repository" \
        --version "$version_name" \
        --service-name "$service_name" \
        --display-name "Relaywright Ugly Path Validation" \
        --install-root "$install_root" \
        --data-directory "$data_directory" \
        --https-port "$https_port" \
        --http-port 0 \
        --smtp-port "$relay_smtp_port" \
        --non-interactive

    wait_service_running
    assert_health_ok "install"
    assert_http_disabled
}

configure_relay_for_ugly_paths() {
    local database_path="$data_directory/relay.db"
    local now
    now="$(date -u +"%Y-%m-%dT%H:%M:%S+00:00")"

    write_step "Configuring Relaywright relay settings for ugly-path validation"
    "${SUDO[@]}" systemctl stop "$service_name"
    "${SUDO[@]}" sqlite3 "$database_path" <<SQL
UPDATE "RelayConfigurations"
SET
    "ListenerBindAddress" = '127.0.0.1',
    "ListenerPort" = $relay_smtp_port,
    "ListenerHostName" = 'relaywright-ugly',
    "UpstreamHost" = '$capture_host',
    "UpstreamPort" = $capture_port,
    "UpstreamSecureSocketOptions" = 0,
    "UseUpstreamAuthentication" = 0,
    "UpstreamAuthenticationMode" = 0,
    "UpstreamUserName" = NULL,
    "ProtectedUpstreamPassword" = NULL,
    "DeliveryConcurrency" = 1,
    "MaxRetryCount" = 6,
    "InitialRetryDelaySeconds" = 2,
    "MaxRetryDelaySeconds" = 5,
    "UpstreamTimeoutSeconds" = 60,
    "MessageExpirationHours" = 2,
    "DeliveredRetentionHours" = 24,
    "FailedRetentionHours" = 24,
    "UpdatedUtc" = '$now'
WHERE "Id" = 1;
SQL
    "${SUDO[@]}" systemctl start "$service_name"
    wait_service_running
    assert_health_ok "configured"
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
        self.write_line("220 relaywright-ugly-capture ESMTP ready")
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
                    if self.server.delay_data_seconds > 0:
                        time.sleep(self.server.delay_data_seconds)
                    data.clear()
                    data_mode = False
                    self.write_line("250 Ok")
                else:
                    data.append(line)
                continue

            command = stripped.decode("utf-8", "replace")
            verb = command.split(" ", 1)[0].upper()
            if verb in ("EHLO", "HELO"):
                self.write_line("250-relaywright-ugly-capture")
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
    parser.add_argument("--delay-data-seconds", type=float, default=0)
    args = parser.parse_args()
    with Server((args.host, args.port), Handler) as server:
        server.store = CaptureStore(args.output)
        server.delay_data_seconds = args.delay_data_seconds
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
    parser.add_argument("--expect", choices=["accepted", "rejected"], required=True)
    args = parser.parse_args()

    transcript = []
    final_code = None
    error = None
    try:
        with socket.create_connection((args.host, args.port), timeout=10) as sock:
            sock.settimeout(10)
            file = sock.makefile("rwb", buffering=0)
            code, _ = read_response(file, transcript)
            require(code, {220}, "greeting")
            code, _ = send_command(file, transcript, "EHLO relaywright-ugly.local")
            require(code, {250}, "EHLO")
            code, _ = send_command(file, transcript, "MAIL FROM:<ugly-sender@example.test>")
            require(code, {250}, "MAIL FROM")
            code, _ = send_command(file, transcript, "RCPT TO:<ugly-recipient@example.test>")
            require(code, {250}, "RCPT TO")
            code, _ = send_command(file, transcript, "DATA")
            require(code, {354}, "DATA")
            message = (
                f"Subject: {args.subject}\r\n"
                "From: ugly-sender@example.test\r\n"
                "To: ugly-recipient@example.test\r\n"
                "\r\n"
                f"{args.body}\r\n"
                ".\r\n"
            )
            transcript.append("C: <message body>")
            file.write(message.encode("utf-8"))
            file.flush()
            final_code, _ = read_response(file, transcript)
            try:
                send_command(file, transcript, "QUIT")
            except Exception:
                pass
    except Exception as exc:
        error = exc
        transcript.append(f"ERROR: {exc}")

    transcript.append(f"FINAL_CODE: {final_code if final_code is not None else 'none'}")
    Path(args.transcript).write_text("\n".join(transcript) + "\n", encoding="utf-8")

    if args.expect == "accepted":
        return 0 if final_code == 250 and error is None else 1

    if final_code == 250:
        return 1
    return 0

if __name__ == "__main__":
    sys.exit(main())
PY
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

start_capture_server() {
    local output_name="$1"
    local delay_seconds="${2:-0}"
    stop_capture_server
    write_capture_server
    local output_directory="$artifacts_directory/$output_name"
    mkdir -p "$output_directory"
    rm -f "$output_directory"/*.eml
    write_step "Starting local capture SMTP server on $capture_host:$capture_port with ${delay_seconds}s DATA delay"
    python3 "$artifacts_directory/capture_smtp.py" \
        --host "$capture_host" \
        --port "$capture_port" \
        --output "$output_directory" \
        --delay-data-seconds "$delay_seconds" \
        > "$artifacts_directory/capture-server-${output_name}.log" 2>&1 &
    capture_pid="$!"
    wait_capture_server
}

send_message() {
    local expectation="$1"
    local name="$2"
    local transcript="$artifacts_directory/transcripts/${name}.txt"
    mkdir -p "$artifacts_directory/transcripts"
    python3 "$artifacts_directory/send_smtp.py" \
        --host "127.0.0.1" \
        --port "$relay_smtp_port" \
        --subject "Relaywright ugly path $name" \
        --body "Relaywright ugly-path validation message $name." \
        --transcript "$transcript" \
        --expect "$expectation"
}

count_captured_messages() {
    local output_name="$1"
    find "$artifacts_directory/$output_name" -type f -name '*.eml' 2>/dev/null | wc -l | tr -d ' '
}

sqlite_scalar() {
    local sql="$1"
    "${SUDO[@]}" sqlite3 "$data_directory/relay.db" "$sql" | tr -d '\r'
}

sqlite_scalar_timeout() {
    local sql="$1"
    "${SUDO[@]}" timeout 5 sqlite3 "$data_directory/relay.db" "$sql" | tr -d '\r'
}

queue_count_for_status() {
    local status="$1"
    sqlite_scalar "SELECT COUNT(*) FROM \"QueuedMessages\" WHERE \"Status\" = $status;"
}

write_queue_counts() {
    local name="$1"
    local pending="unavailable"
    local in_progress="unavailable"
    local retry_scheduled="unavailable"
    local delivered="unavailable"
    local failed="unavailable"
    local expired="unavailable"
    local delivery_attempts="unavailable"
    local spool_files="unavailable"

    pending="$(sqlite_scalar_timeout 'SELECT COUNT(*) FROM "QueuedMessages" WHERE "Status" = 0;' 2>/dev/null || true)"
    in_progress="$(sqlite_scalar_timeout 'SELECT COUNT(*) FROM "QueuedMessages" WHERE "Status" = 1;' 2>/dev/null || true)"
    retry_scheduled="$(sqlite_scalar_timeout 'SELECT COUNT(*) FROM "QueuedMessages" WHERE "Status" = 2;' 2>/dev/null || true)"
    delivered="$(sqlite_scalar_timeout 'SELECT COUNT(*) FROM "QueuedMessages" WHERE "Status" = 3;' 2>/dev/null || true)"
    failed="$(sqlite_scalar_timeout 'SELECT COUNT(*) FROM "QueuedMessages" WHERE "Status" = 4;' 2>/dev/null || true)"
    expired="$(sqlite_scalar_timeout 'SELECT COUNT(*) FROM "QueuedMessages" WHERE "Status" = 5;' 2>/dev/null || true)"
    delivery_attempts="$(sqlite_scalar_timeout 'SELECT COUNT(*) FROM "DeliveryAttempts";' 2>/dev/null || true)"
    spool_files="$("${SUDO[@]}" find "$data_directory/spool" -type f -name '*.eml' 2>/dev/null | wc -l | tr -d ' ' || true)"

    {
        echo "pending=${pending:-unavailable}"
        echo "in_progress=${in_progress:-unavailable}"
        echo "retry_scheduled=${retry_scheduled:-unavailable}"
        echo "delivered=${delivered:-unavailable}"
        echo "failed=${failed:-unavailable}"
        echo "expired=${expired:-unavailable}"
        echo "delivery_attempts=${delivery_attempts:-unavailable}"
        echo "spool_files=${spool_files:-unavailable}"
    } > "$artifacts_directory/$name"
}

total_queue_count() {
    sqlite_scalar 'SELECT COUNT(*) FROM "QueuedMessages";'
}

wait_for_sql_count_at_least() {
    local description="$1"
    local sql="$2"
    local minimum="$3"
    local timeout_seconds="$4"
    local deadline=$((SECONDS + timeout_seconds))
    local value

    while (( SECONDS < deadline )); do
        value="$(sqlite_scalar "$sql" 2>/dev/null || echo 0)"
        if [[ "$value" =~ ^[0-9]+$ ]] && (( value >= minimum )); then
            return
        fi
        sleep 1
    done

    write_queue_counts "queue-counts-timeout-${description}.txt"
    die "Timed out waiting for $description to reach at least $minimum."
}

wait_for_captured_count_at_least() {
    local output_name="$1"
    local minimum="$2"
    local timeout_seconds="$3"
    local deadline=$((SECONDS + timeout_seconds))
    local value

    while (( SECONDS < deadline )); do
        value="$(count_captured_messages "$output_name")"
        if [[ "$value" =~ ^[0-9]+$ ]] && (( value >= minimum )); then
            return
        fi
        sleep 1
    done

    die "Timed out waiting for $output_name captured count to reach at least $minimum."
}

write_db_lock_script() {
    local script_path="$artifacts_directory/db-lock.py"
    cat > "$script_path" <<'PY'
import argparse
import pathlib
import sqlite3
import time

parser = argparse.ArgumentParser()
parser.add_argument("--db", required=True)
parser.add_argument("--ready", required=True)
parser.add_argument("--seconds", type=int, required=True)
args = parser.parse_args()

connection = sqlite3.connect(args.db, timeout=1, isolation_level=None)
connection.execute("PRAGMA busy_timeout = 1000")
deadline = time.monotonic() + 10
while True:
    try:
        connection.execute("BEGIN EXCLUSIVE")
        break
    except sqlite3.OperationalError:
        if time.monotonic() >= deadline:
            raise
        time.sleep(0.25)
pathlib.Path(args.ready).write_text("locked\n", encoding="utf-8")
time.sleep(args.seconds)
connection.rollback()
connection.close()
PY
}

start_db_lock() {
    local seconds="$1"
    local ready_file="$artifacts_directory/db-lock-ready.txt"
    rm -f "$ready_file"
    write_db_lock_script
    write_step "Holding an exclusive SQLite lock for ${seconds}s"
    "${SUDO[@]}" python3 "$artifacts_directory/db-lock.py" \
        --db "$data_directory/relay.db" \
        --ready "$ready_file" \
        --seconds "$seconds" &
    db_lock_pid="$!"

    local deadline=$((SECONDS + 10))
    while (( SECONDS < deadline )); do
        [[ -s "$ready_file" ]] && return
        sleep 1
    done

    die "SQLite lock helper did not report ready."
}

test_spool_obstructed() {
    write_step "Testing obstructed spool path"
    local spool_path="$data_directory/spool"
    local before_count
    before_count="$(total_queue_count)"

    "${SUDO[@]}" rm -rf -- "$spool_path"
    printf 'not a directory\n' | "${SUDO[@]}" tee "$spool_path" >/dev/null

    assert_health_not_ok "spool-obstructed"
    send_message "rejected" "spool-obstructed"

    local after_count
    after_count="$(total_queue_count)"
    [[ "$after_count" == "$before_count" ]] || die "Spool obstruction changed queue count from $before_count to $after_count."

    "${SUDO[@]}" rm -f -- "$spool_path"
    "${SUDO[@]}" mkdir -p "$spool_path"
    assert_health_ok "spool-restored"
    write_queue_counts "queue-counts-after-spool-obstructed.txt"
    append_result "spool_obstructed" "passed"
}

test_db_lock_recovery() {
    write_step "Testing SQLite lock health degradation and recovery"
    start_db_lock 20
    assert_health_not_ok "db-locked"
    stop_db_lock
    assert_health_ok "db-lock-restored"
    write_queue_counts "queue-counts-after-db-lock.txt"
    append_result "db_lock_recovery" "passed"
}

test_upstream_outage_retry() {
    write_step "Testing upstream outage retry and recovery"
    stop_capture_server
    send_message "accepted" "upstream-outage"
    wait_for_sql_count_at_least "upstream-retry" 'SELECT COUNT(*) FROM "QueuedMessages" WHERE "Status" = 2;' 1 45

    start_capture_server "captured-upstream" 0
    wait_for_sql_count_at_least "upstream-delivered" 'SELECT COUNT(*) FROM "QueuedMessages" WHERE "Status" = 3;' 1 90
    wait_for_captured_count_at_least "captured-upstream" 1 30

    local attempts
    attempts="$(sqlite_scalar 'SELECT COUNT(*) FROM "DeliveryAttempts";')"
    [[ "$attempts" =~ ^[0-9]+$ && "$attempts" -ge 2 ]] || die "Expected at least two delivery attempts after upstream outage, found $attempts."

    write_queue_counts "queue-counts-after-upstream-outage.txt"
    append_result "upstream_outage_retry" "passed"
}

test_bad_certificate_password() {
    write_step "Testing bad HTTPS certificate password failure and recovery"
    local env_file="/etc/${service_name}.env"
    local backup_file="/tmp/${service_name}.env.backup.$$"
    "${SUDO[@]}" cp "$env_file" "$backup_file"

    "${SUDO[@]}" sed -i 's/^ASPNETCORE_Kestrel__Certificates__Default__Password=.*/ASPNETCORE_Kestrel__Certificates__Default__Password="relaywright-validation-invalid"/' "$env_file"
    "${SUDO[@]}" systemctl restart "$service_name" >/dev/null 2>&1 || true
    sleep 8
    assert_health_not_ok "bad-certificate-password"
    "${SUDO[@]}" systemctl status "$service_name" --no-pager > "$artifacts_directory/service-bad-certificate-password.txt" 2>&1 || true

    "${SUDO[@]}" cp "$backup_file" "$env_file"
    "${SUDO[@]}" rm -f "$backup_file"
    "${SUDO[@]}" systemctl reset-failed "$service_name" >/dev/null 2>&1 || true
    "${SUDO[@]}" systemctl restart "$service_name"
    wait_service_running
    assert_health_ok "bad-certificate-password-restored"
    append_result "bad_certificate_password_recovery" "passed"
}

test_restart_during_active_delivery() {
    write_step "Testing restart during active delivery and stale in-progress recovery"
    stop_capture_server
    start_capture_server "captured-restart" 45

    send_message "accepted" "restart-active-delivery"
    wait_for_sql_count_at_least "restart-in-progress" 'SELECT COUNT(*) FROM "QueuedMessages" WHERE "Status" = 1;' 1 45

    "${SUDO[@]}" systemctl restart "$service_name" >/dev/null 2>&1 || true
    wait_service_running
    assert_health_ok "restart-after-active-delivery"

    stop_capture_server
    start_capture_server "captured-restart" 0

    local stale_utc
    stale_utc="$(date -u -d '20 minutes ago' +"%Y-%m-%dT%H:%M:%S+00:00")"
    "${SUDO[@]}" sqlite3 "$data_directory/relay.db" <<SQL
UPDATE "QueuedMessages"
SET
    "LastAttemptStartedUtc" = '$stale_utc',
    "NextAttemptAtUtc" = '$stale_utc'
WHERE "Status" IN (1, 2);
SQL

    wait_for_sql_count_at_least "restart-delivered" 'SELECT COUNT(*) FROM "QueuedMessages" WHERE "Status" = 3;' 2 120
    wait_for_captured_count_at_least "captured-restart" 1 60
    write_queue_counts "queue-counts-after-restart-active-delivery.txt"
    append_result "restart_during_active_delivery" "passed"
}

fingerprint_restore_markers() {
    local name="$1"
    local spool_marker="$data_directory/spool/backup-restore-marker.eml"
    local backup_marker="$data_directory/backups/ugly-restore-marker.txt"
    {
        echo "spool_marker=$("${SUDO[@]}" sha256sum "$spool_marker" | awk '{print $1}')"
        echo "backup_marker=$("${SUDO[@]}" sha256sum "$backup_marker" | awk '{print $1}')"
        echo "key_count=$("${SUDO[@]}" find "$data_directory/keys" -type f -name '*.xml' 2>/dev/null | wc -l | tr -d ' ')"
        echo "cert_count=$("${SUDO[@]}" find "$data_directory/certs" -type f 2>/dev/null | wc -l | tr -d ' ')"
    } > "$artifacts_directory/$name"
}

test_backup_restore() {
    write_step "Testing cold data-directory backup and restore"
    local backup_path="/tmp/${service_name}-data-backup-$$.tgz"

    "${SUDO[@]}" mkdir -p "$data_directory/spool" "$data_directory/backups"
    printf 'Relaywright ugly-path backup spool marker.\n' |
        "${SUDO[@]}" tee "$data_directory/spool/backup-restore-marker.eml" >/dev/null
    printf 'Relaywright ugly-path backup marker.\n' |
        "${SUDO[@]}" tee "$data_directory/backups/ugly-restore-marker.txt" >/dev/null

    fingerprint_restore_markers "backup-restore-fingerprint-before.txt"

    "${SUDO[@]}" systemctl stop "$service_name"
    "${SUDO[@]}" tar -C "$data_directory" -czf "$backup_path" .
    "${SUDO[@]}" rm -rf -- "$data_directory"
    "${SUDO[@]}" mkdir -p "$data_directory"
    "${SUDO[@]}" tar -C "$data_directory" -xzf "$backup_path"
    "${SUDO[@]}" rm -f "$backup_path"

    "${SUDO[@]}" systemctl start "$service_name"
    wait_service_running
    assert_health_ok "backup-restore"
    fingerprint_restore_markers "backup-restore-fingerprint-after.txt"
    cmp -s "$artifacts_directory/backup-restore-fingerprint-before.txt" "$artifacts_directory/backup-restore-fingerprint-after.txt" ||
        die "Backup/restore fingerprints changed."

    append_result "backup_restore" "passed"
}

save_diagnostics() {
    local suffix="$1"
    {
        echo "mode=$mode"
        echo "version=$version"
        echo "repository=$repository"
        echo "service_name=$service_name"
        echo "install_root=$install_root"
        echo "data_directory=$data_directory"
        echo "https_port=$https_port"
        echo "relay_smtp_port=$relay_smtp_port"
        echo "capture_port=$capture_port"
        echo "cleanup_after=$cleanup_after"
    } > "$artifacts_directory/validation-input-${suffix}.txt"

    "${SUDO[@]}" systemctl status "$service_name" --no-pager > "$artifacts_directory/service-${suffix}.txt" 2>&1 || true
    "${SUDO[@]}" journalctl -u "$service_name" -n 180 --no-pager > "$artifacts_directory/journal-${suffix}.txt" 2>&1 || true
    write_queue_counts "queue-counts-${suffix}.txt" || true
    "${SUDO[@]}" timeout 5 sqlite3 "$data_directory/relay.db" \
        'SELECT "Status", COUNT(*) FROM "QueuedMessages" GROUP BY "Status" ORDER BY "Status";' \
        > "$artifacts_directory/queue-status-raw-${suffix}.txt" 2>&1 || true
}

on_error() {
    local line="$1"
    set +e
    write_artifact failure.txt "Ugly-path validation failed at line $line."
    stop_capture_server
    stop_db_lock
    save_diagnostics failure
}

trap 'on_error $LINENO' ERR
trap 'stop_capture_server; stop_db_lock' EXIT

write_artifact validation-input.txt "mode=$mode
version=$version
repository=$repository
service_name=$service_name
install_root=$install_root
data_directory=$data_directory
https_port=$https_port
relay_smtp_port=$relay_smtp_port
capture_port=$capture_port
cleanup_after=$cleanup_after"

case "$mode" in
    cleanup-only)
        invoke_cleanup
        save_diagnostics cleanup
        write_step "Linux ugly-path cleanup completed successfully"
        exit 0
        ;;
esac

reset_artifacts
install_dependencies
download_installer_script
invoke_cleanup
run_installer
configure_relay_for_ugly_paths
write_smtp_client

test_spool_obstructed
test_db_lock_recovery
test_upstream_outage_retry
test_bad_certificate_password
test_restart_during_active_delivery
test_backup_restore

assert_health_ok "final"
save_diagnostics final
write_step "Linux ugly-path validation completed successfully"

if is_true "$cleanup_after"; then
    invoke_cleanup
    save_diagnostics cleanup
fi
