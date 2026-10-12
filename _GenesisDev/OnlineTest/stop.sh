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
        printf '[OnlineTest] Останавливаю %s и дочерние процессы (PID %s)...\n' "$name" "$pid"

        # dotnet run owns a child Content.Server/Content.Client process. Stop the
        # whole tree so an orphaned game process cannot keep UDP 1212 occupied.
        local -a process_tree=()
        collect_tree() {
            local parent="$1" child
            while IFS= read -r child; do
                [[ "$child" =~ ^[0-9]+$ ]] && collect_tree "$child"
            done < <(pgrep -P "$parent" 2>/dev/null || true)
            process_tree+=("$parent")
        }
        collect_tree "$pid"

        local child_pid
        for child_pid in "${process_tree[@]}"; do
            kill -TERM "$child_pid" 2>/dev/null || true
        done

        for _ in {1..10}; do
            local alive=0 state
            for child_pid in "${process_tree[@]}"; do
                if kill -0 "$child_pid" 2>/dev/null; then
                    state="$(ps -o stat= -p "$child_pid" 2>/dev/null | tr -d '[:space:]' || true)"
                    [[ "$state" == Z* ]] || alive=1
                fi
            done
            (( alive == 0 )) && break
            sleep 1
        done

        for child_pid in "${process_tree[@]}"; do
            if kill -0 "$child_pid" 2>/dev/null; then
                state="$(ps -o stat= -p "$child_pid" 2>/dev/null | tr -d '[:space:]' || true)"
                [[ "$state" == Z* ]] || kill -KILL "$child_pid" 2>/dev/null || true
            fi
        done
    fi
    rm -f "$file"
}

for name in client server websockify vnc openbox xvfb; do
    stop_one "$name"
done
printf '[OnlineTest] Остановлено. Логи сохранены в %s/logs.\n' "$RUNTIME_DIR"
