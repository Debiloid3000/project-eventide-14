#!/usr/bin/env bash
set -Eeuo pipefail
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
RUNTIME_DIR="$SCRIPT_DIR/.runtime"

stop_one() {
    local name="$1"
    local file="$RUNTIME_DIR/${name}.pid"
    [[ -f "$file" ]] || return 0
    local pid
    pid="$(cat "$file")"
    if [[ "$pid" =~ ^[0-9]+$ ]] && kill -0 "$pid" 2>/dev/null; then
        printf '[OnlineTest] Останавливаю %s (PID %s)...\n' "$name" "$pid"
        kill -TERM "$pid" 2>/dev/null || true
        for _ in {1..10}; do
            kill -0 "$pid" 2>/dev/null || break
            sleep 1
        done
        kill -KILL "$pid" 2>/dev/null || true
    fi
    rm -f "$file"
}

for name in client server websockify vnc openbox xvfb; do
    stop_one "$name"
done
printf '[OnlineTest] Остановлено. Логи сохранены в %s/logs.\n' "$RUNTIME_DIR"
