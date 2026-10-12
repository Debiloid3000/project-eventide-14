#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd)"
RUNTIME_DIR="$SCRIPT_DIR/.runtime"
LOG_DIR="$RUNTIME_DIR/logs"
DISPLAY_NUM=":1"
VNC_PORT="${ONLINE_TEST_VNC_PORT:-5900}"
WEB_PORT="${ONLINE_TEST_WEB_PORT:-6080}"
SERVER_PORT="${ONLINE_TEST_SERVER_PORT:-1212}"
mkdir -p "$LOG_DIR"

log() { printf '[OnlineTest] %s\n' "$*"; }
die() { printf '[OnlineTest][ERROR] %s\n' "$*" >&2; exit 1; }

for cmd in dotnet python3; do
    command -v "$cmd" >/dev/null 2>&1 || die "Не найдена команда '$cmd'. Установи .NET SDK и Python 3 в Codespace."
done

# Install the headless Linux desktop + browser-accessible noVNC gateway only when needed.
find_novnc_dir() {
    local candidate
    for candidate in /usr/share/novnc /usr/share/novnc-pkg /usr/share/noVNC; do
        if [[ -f "$candidate/vnc.html" ]]; then
            printf '%s' "$candidate"
            return 0
        fi
    done
    return 1
}

NOVNC_DIR="$(find_novnc_dir || true)"
if ! command -v Xvfb >/dev/null 2>&1 || \
   ! command -v openbox >/dev/null 2>&1 || \
   ! command -v x11vnc >/dev/null 2>&1 || \
   ! command -v websockify >/dev/null 2>&1 || [[ -z "$NOVNC_DIR" ]]; then
    log "Устанавливаю Xvfb, Openbox, noVNC и необходимые графические библиотеки..."
    if [[ "$(id -u)" -eq 0 ]]; then
        SUDO=()
    elif command -v sudo >/dev/null 2>&1; then
        SUDO=(sudo)
    else
        die "Нужен sudo для установки графических пакетов."
    fi
    "${SUDO[@]}" apt-get update
    "${SUDO[@]}" apt-get install -y \
        xvfb openbox x11vnc novnc websockify \
        mesa-utils libgl1-mesa-dri libglx-mesa0 \
        libx11-6 libxext6 libxrandr2 libxi6 libxcursor1 libxinerama1
    NOVNC_DIR="$(find_novnc_dir || true)"
fi
[[ -n "$NOVNC_DIR" ]] || die "Не найден /usr/share/novnc/vnc.html после установки пакета novnc."

# RUN_THIS.py initializes the RobustToolbox submodule through BuildChecker.
if [[ ! -f "$ROOT/RobustToolbox/Robust.Shared/Robust.Shared.csproj" ]]; then
    log "Инициализирую подмодули/движок через RUN_THIS.py..."
    (cd "$ROOT" && python3 RUN_THIS.py)
fi
[[ -f "$ROOT/RobustToolbox/Robust.Shared/Robust.Shared.csproj" ]] || \
    die "RobustToolbox не инициализирован. Проверь вывод RUN_THIS.py и BuildChecker/git_helper.py."

# Choose a conservative server tickrate based on the CPU and memory limits visible to this container.
# The value is applied at server startup; it is not dynamically changed while the server is running.
detect_memory_mb() {
    local value
    if [[ -r /sys/fs/cgroup/memory.max ]]; then
        value="$(cat /sys/fs/cgroup/memory.max)"
        if [[ "$value" =~ ^[0-9]+$ ]] && (( value > 0 )); then
            echo $((value / 1024 / 1024))
            return
        fi
    fi
    if [[ -r /sys/fs/cgroup/memory/memory.limit_in_bytes ]]; then
        value="$(cat /sys/fs/cgroup/memory/memory.limit_in_bytes)"
        if [[ "$value" =~ ^[0-9]+$ ]] && (( value > 0 && value < 1099511627776 )); then
            echo $((value / 1024 / 1024))
            return
        fi
    fi
    awk '/MemTotal:/ {print int($2 / 1024); exit}' /proc/meminfo
}

detect_cpu_count() {
    local quota period
    if [[ -r /sys/fs/cgroup/cpu.max ]]; then
        read -r quota period < /sys/fs/cgroup/cpu.max
        if [[ "$quota" =~ ^[0-9]+$ ]] && [[ "$period" =~ ^[0-9]+$ ]] && (( period > 0 )); then
            echo $(((quota + period - 1) / period))
            return
        fi
    fi
    if [[ -r /sys/fs/cgroup/cpu/cpu.cfs_quota_us && -r /sys/fs/cgroup/cpu/cpu.cfs_period_us ]]; then
        quota="$(cat /sys/fs/cgroup/cpu/cpu.cfs_quota_us)"
        period="$(cat /sys/fs/cgroup/cpu/cpu.cfs_period_us)"
        if [[ "$quota" =~ ^[0-9]+$ ]] && (( quota > 0 && period > 0 )); then
            echo $(((quota + period - 1) / period))
            return
        fi
    fi
    nproc
}

if [[ -n "${SS14_TICKRATE:-}" ]]; then
    [[ "$SS14_TICKRATE" =~ ^[0-9]+$ ]] || die "SS14_TICKRATE должен быть целым числом от 5 до 60."
    (( SS14_TICKRATE >= 5 && SS14_TICKRATE <= 60 )) || die "SS14_TICKRATE должен быть от 5 до 60."
    TICKRATE="$SS14_TICKRATE"
else
    MEMORY_MB="$(detect_memory_mb)"
    CPU_COUNT="$(detect_cpu_count)"
    if (( CPU_COUNT <= 2 || MEMORY_MB <= 6144 )); then
        TICKRATE=10
    elif (( CPU_COUNT <= 4 || MEMORY_MB <= 8192 )); then
        TICKRATE=15
    elif (( CPU_COUNT >= 8 && MEMORY_MB >= 12288 )); then
        TICKRATE=30
    else
        TICKRATE=20
    fi
    log "Ресурсы контейнера: CPU≈${CPU_COUNT}, RAM≈${MEMORY_MB} MiB; выбран tickrate=${TICKRATE}."
fi

# Avoid a second copy of the test environment.
if [[ -f "$RUNTIME_DIR/server.pid" ]] && kill -0 "$(cat "$RUNTIME_DIR/server.pid")" 2>/dev/null; then
    die "Сервер уже запущен (PID $(cat "$RUNTIME_DIR/server.pid")). Сначала выполни stop.sh."
fi
if [[ -f "$RUNTIME_DIR/client.pid" ]] && kill -0 "$(cat "$RUNTIME_DIR/client.pid")" 2>/dev/null; then
    die "Клиент уже запущен (PID $(cat "$RUNTIME_DIR/client.pid")). Сначала выполни stop.sh."
fi

if [[ "${SS14_SKIP_BUILD:-0}" != "1" ]]; then
    log "Собираю сервер (последовательно, чтобы снизить пиковое потребление RAM)..."
    (cd "$ROOT" && dotnet build Content.Server/Content.Server.csproj --configuration Debug --nologo --verbosity minimal -m:1)
    log "Собираю клиент..."
    (cd "$ROOT" && dotnet build Content.Client/Content.Client.csproj --configuration Debug --nologo --verbosity minimal -m:1)
else
    log "SS14_SKIP_BUILD=1: пропускаю сборку."
fi

# Start a virtual X desktop. noVNC exposes only this desktop, not the game UDP socket.
export DISPLAY="$DISPLAY_NUM"
export LIBGL_ALWAYS_SOFTWARE=1
export SDL_VIDEODRIVER=x11

start_process() {
    local name="$1"
    shift
    local pidfile="$RUNTIME_DIR/${name}.pid"
    if [[ -f "$pidfile" ]] && kill -0 "$(cat "$pidfile")" 2>/dev/null; then
        die "$name уже запущен (PID $(cat "$pidfile"))."
    fi
    nohup "$@" >>"$LOG_DIR/${name}.log" 2>&1 </dev/null &
    echo "$!" > "$pidfile"
}

start_process xvfb Xvfb "$DISPLAY_NUM" -screen 0 1280x800x24 -ac +extension GLX +render -noreset
sleep 2
kill -0 "$(cat "$RUNTIME_DIR/xvfb.pid")" 2>/dev/null || { tail -n 80 "$LOG_DIR/xvfb.log"; die "Xvfb не запустился."; }
start_process openbox openbox
start_process vnc x11vnc -display "$DISPLAY_NUM" -rfbport "$VNC_PORT" -listen 127.0.0.1 -forever -shared -nopw
start_process websockify websockify --web="$NOVNC_DIR" "$WEB_PORT" "127.0.0.1:$VNC_PORT"

log "Запускаю локальный тестовый сервер на UDP-порту $SERVER_PORT с tickrate=$TICKRATE и подробным сетевым логированием..."
(
    cd "$ROOT"
    exec dotnet run --no-build --project Content.Server/Content.Server.csproj -- --cvar "net.bindto=127.0.0.1" --cvar "net.port=$SERVER_PORT" --cvar "net.tickrate=$TICKRATE" --cvar "net.logging=true" --cvar "net.verbose=true"
) >>"$LOG_DIR/server.log" 2>&1 </dev/null &
echo "$!" > "$RUNTIME_DIR/server.pid"

# Give the server time to report immediate startup failures before starting the client UI.
sleep 5
if ! kill -0 "$(cat "$RUNTIME_DIR/server.pid")" 2>/dev/null; then
    tail -n 100 "$LOG_DIR/server.log" >&2 || true
    die "Content.Server завершился при запуске. См. _GenesisDev/OnlineTest/.runtime/logs/server.log"
fi

log "Запускаю Content.Client внутри виртуального рабочего стола..."
(
    cd "$ROOT"
    # Codespaces has no physical audio output device; use OpenAL Soft null output.
    exec env DISPLAY="$DISPLAY_NUM" LIBGL_ALWAYS_SOFTWARE=1 SDL_VIDEODRIVER=x11 ALSOFT_DRIVERS=null \
        dotnet run --no-build --project Content.Client/Content.Client.csproj
) >>"$LOG_DIR/client.log" 2>&1 </dev/null &
echo "$!" > "$RUNTIME_DIR/client.pid"

sleep 2
if ! kill -0 "$(cat "$RUNTIME_DIR/openbox.pid")" 2>/dev/null; then
    tail -n 80 "$LOG_DIR/openbox.log" >&2 || true
    die "Openbox не запустился. См. лог openbox.log"
fi
if ! kill -0 "$(cat "$RUNTIME_DIR/websockify.pid")" 2>/dev/null; then
    tail -n 80 "$LOG_DIR/websockify.log" >&2 || true
    die "noVNC web gateway не запустился. См. лог websockify.log"
fi

# Let the client finish initialization; fail loudly rather than reporting success on a black screen.
sleep 8
if ! kill -0 "$(cat "$RUNTIME_DIR/client.pid")" 2>/dev/null; then
    tail -n 120 "$LOG_DIR/client.log" >&2 || true
    die "Content.Client завершился при запуске. Проверь client.log."
fi

log "Стенд запущен."
log "Открой вкладку Ports и открой порт $WEB_PORT (TCP); URL ведёт на рабочий стол с Content.Client."
if [[ -n "${CODESPACE_NAME:-}" ]]; then
    printf 'noVNC URL: https://%s-%s.app.github.dev/vnc.html?autoconnect=1&resize=remote\n' "$CODESPACE_NAME" "$WEB_PORT"
fi
log "В окне клиента используй Direct Connect и адрес 127.0.0.1:$SERVER_PORT."
log "Логи: $LOG_DIR/server.log, $LOG_DIR/client.log, $LOG_DIR/websockify.log"
log "Остановить всё: bash _GenesisDev/OnlineTest/stop.sh"
