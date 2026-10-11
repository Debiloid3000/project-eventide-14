#!/usr/bin/env bash
set -Eeuo pipefail
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
RUNTIME_DIR="$SCRIPT_DIR/.runtime"

for name in server client xvfb openbox vnc websockify; do
    file="$RUNTIME_DIR/${name}.pid"
    if [[ -f "$file" ]] && kill -0 "$(cat "$file")" 2>/dev/null; then
        printf '[RUNNING] %-10s PID %s\n' "$name" "$(cat "$file")"
    else
        printf '[STOPPED] %s\n' "$name"
    fi
done
printf '\nЛоги: %s/logs/\n' "$RUNTIME_DIR"
