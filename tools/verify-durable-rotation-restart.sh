#!/usr/bin/env bash

set -euo pipefail

script_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
repo_root="$(CDPATH= cd -- "$script_dir/.." && pwd)"
test_project="$repo_root/project/tests/App.World.Tests/App.World.Tests.csproj"
test_filter="FullyQualifiedName~ExternalSurrealRotationRestartProofTests"
configuration="${FANTASIM_ROTATION_RESTART_CONFIGURATION:-Debug}"
keep_evidence="${FANTASIM_ROTATION_RESTART_KEEP_EVIDENCE:-0}"
run_id="${FANTASIM_ROTATION_RESTART_RUN_ID:-$(date -u +%Y%m%dT%H%M%SZ)-$$-${RANDOM:-0}}"
namespace_suffix="$(printf '%s' "$run_id" | tr -c 'A-Za-z0-9_' '_')"
namespace="fantasim_rotation_restart_${namespace_suffix}"
database="world"
host="127.0.0.1"
temp_root="${TMPDIR:-/tmp}"
evidence_dir="${FANTASIM_ROTATION_RESTART_EVIDENCE_DIR:-${temp_root%/}/fantasim-rotation-restart-${run_id}}"
database_dir="$evidence_dir/rocksdb"
sequence_file="$evidence_dir/sequence.status"
server_pid=""

fail() {
    printf 'durable rotation restart proof FAILED: %s\n' "$*" >&2
    exit 1
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || fail "required command '$1' was not found"
}

find_free_port() {
    python3 - "$host" <<'PY'
import socket
import sys

host = sys.argv[1]
with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
    sock.bind((host, 0))
    print(sock.getsockname()[1])
PY
}

port_is_free() {
    python3 - "$host" "$port" <<'PY'
import socket
import sys

host = sys.argv[1]
port = int(sys.argv[2])
with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
    try:
        sock.bind((host, port))
    except OSError:
        raise SystemExit(1)
PY
}

cleanup() {
    status=$?
    trap - EXIT
    if [ -n "$server_pid" ] && kill -0 "$server_pid" 2>/dev/null; then
        kill -INT "$server_pid" 2>/dev/null || true
        wait "$server_pid" 2>/dev/null || true
    fi
    if [ "$keep_evidence" = "1" ] || [ "$keep_evidence" = "true" ]; then
        printf 'Evidence retained at %s\n' "$evidence_dir"
    else
        rm -rf "$evidence_dir"
    fi
    exit "$status"
}

trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

start_server() {
    label=$1
    log_file="$evidence_dir/${label}-server.log"
    pid_file="$evidence_dir/${label}-server.pid"

    port_is_free || fail "port $port is not free before $label server start"
    surreal start --no-banner --unauthenticated --bind "$host:$port" "rocksdb:$database_dir" \
        >"$log_file" 2>&1 &
    server_pid=$!
    printf '%s\n' "$server_pid" >"$pid_file"

    readiness_attempt=0
    while [ "$readiness_attempt" -lt 100 ]; do
        if ! kill -0 "$server_pid" 2>/dev/null; then
            wait "$server_pid" 2>/dev/null || true
            server_pid=""
            fail "$label SurrealDB process exited before readiness; see $log_file"
        fi
        if curl -fsS "http://$host:$port/health" >/dev/null 2>&1; then
            printf '%s_ready=1\n' "$label" >>"$sequence_file"
            return
        fi
        readiness_attempt=$((readiness_attempt + 1))
        sleep 0.1
    done

    fail "$label SurrealDB process did not become ready; see $log_file"
}

stop_server() {
    label=$1
    pid=$server_pid
    [ -n "$pid" ] || fail "no active server PID to stop for $label"

    kill -INT "$pid"
    set +e
    wait "$pid"
    stop_status=$?
    set -e
    server_pid=""

    [ "$stop_status" -eq 0 ] || fail "$label SurrealDB process exited with status $stop_status"
    if kill -0 "$pid" 2>/dev/null; then
        fail "$label SurrealDB PID $pid remained alive after wait"
    fi
    printf '%s_stop_exit=%s\n' "$label" "$stop_status" >>"$sequence_file"
}

wait_for_port_free() {
    attempt=0
    while [ "$attempt" -lt 100 ]; do
        if port_is_free; then
            return
        fi
        attempt=$((attempt + 1))
        sleep 0.1
    done
    fail "port $port was not released after server shutdown"
}

run_phase() {
    phase=$1
    receipt="$evidence_dir/${phase}.receipt"
    log_file="$evidence_dir/${phase}-test.log"
    rm -f "$receipt"

    if [ "$phase" = "write" ]; then
        if ! (
            cd "$repo_root"
            FANTASIM_ROTATION_RESTART_PROOF_PHASE=write \
            FANTASIM_ROTATION_RESTART_PROOF_CONNECTION_STRING="$connection_string" \
            FANTASIM_ROTATION_RESTART_PROOF_RECEIPT="$receipt" \
                dotnet test "$test_project" \
                    --configuration "$configuration" \
                    --filter "$test_filter" \
                    --logger 'console;verbosity=normal'
        ) 2>&1 | tee "$log_file"; then
            fail "write test process failed; see $log_file"
        fi
    elif [ "$phase" = "read" ]; then
        if ! (
            cd "$repo_root"
            FANTASIM_ROTATION_RESTART_PROOF_PHASE=read \
            FANTASIM_ROTATION_RESTART_PROOF_CONNECTION_STRING="$connection_string" \
            FANTASIM_ROTATION_RESTART_PROOF_RECEIPT="$receipt" \
                dotnet test "$test_project" \
                    --configuration "$configuration" \
                    --no-build \
                    --no-restore \
                    --filter "$test_filter" \
                    --logger 'console;verbosity=normal'
        ) 2>&1 | tee "$log_file"; then
            fail "read test process failed; see $log_file"
        fi
    else
        fail "unsupported proof phase '$phase'"
    fi

    [ -f "$receipt" ] || fail "$phase test exited successfully without its phase receipt (normal no-op is not proof)"
    receipt_value="$(tr -d '\r\n' <"$receipt")"
    [ "$receipt_value" = "$phase" ] || fail "$phase receipt contained '$receipt_value'"
    printf '%s_test_exit=0\n' "$phase" >>"$sequence_file"
    printf '%s_receipt=%s\n' "$phase" "$receipt_value" >>"$sequence_file"
}

require_command surreal
require_command dotnet
require_command curl
require_command python3

[ ! -e "$evidence_dir" ] || fail "evidence directory already exists: $evidence_dir"
mkdir -p "$database_dir"
: >"$sequence_file"

port="${FANTASIM_ROTATION_RESTART_PORT:-$(find_free_port)}"
case "$port" in
    ''|*[!0-9]*) fail "port must be numeric: '$port'" ;;
esac
port_is_free || fail "selected port $port is already in use"
connection_string="Endpoint=http://$host:$port;Namespace=$namespace;Database=$database"

{
    printf 'run_id=%s\n' "$run_id"
    printf 'namespace=%s\n' "$namespace"
    printf 'database=%s\n' "$database"
    printf 'host=%s\n' "$host"
    printf 'port=%s\n' "$port"
    printf 'rocksdb_dir=%s\n' "$database_dir"
    printf 'connection_string=%s\n' "$connection_string"
} >"$evidence_dir/manifest.txt"

printf 'Starting durable rotation restart proof %s on %s:%s\n' "$run_id" "$host" "$port"

start_server first
first_pid=$server_pid
printf 'first_pid=%s\n' "$first_pid" >>"$sequence_file"
run_phase write
stop_server first
wait_for_port_free

start_server second
second_pid=$server_pid
printf 'second_pid=%s\n' "$second_pid" >>"$sequence_file"
[ "$second_pid" != "$first_pid" ] || fail "server restart reused PID $first_pid"
printf 'distinct_server_pids=1\n' >>"$sequence_file"
run_phase read
stop_server second
wait_for_port_free

printf 'proof_complete=1\n' >>"$sequence_file"
printf 'Durable rotation restart proof PASSED (first PID %s, second PID %s).\n' "$first_pid" "$second_pid"

