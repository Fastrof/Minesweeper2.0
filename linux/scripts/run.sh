#!/usr/bin/env bash
# 启动 Linux 版（自动探测可执行文件位置）
#
#   scripts/run.sh                    启动窗口（需要 WSLg / X11 / Wayland）
#   scripts/run.sh --screenshot DIR   无头截图（不需要显示器）
#   scripts/run.sh --build            现场构建再启动
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"

# 优先用 self-contained 的 publish 产物；没有就退回构建输出（需要 DOTNET_ROOT）
detect_exe() {
    # 优先"刚构建出来的"（最新代码），其次才是打包产物
    local candidates=(
        "$HOME/artifacts/ms2linux/bin/MineSweeper.App.Linux/release/Minesweeper2"
        "$HOME/artifacts/ms2linux/deb/publish/Minesweeper2"
        "$REPO_ROOT/src/MineSweeper.App.Linux/bin/Release/net10.0/linux-x64/Minesweeper2"
    )
    for p in "${candidates[@]}"; do
        if [ -x "$p" ]; then
            printf '%s' "$p"
            return 0
        fi
    done
    return 1
}

MODE="${1:-run}"
case "$MODE" in
  --build)
    shift
    exec "$DOTNET" run --project "$REPO_ROOT/src/MineSweeper.App.Linux" -c Release -- "$@"
    ;;
  --screenshot)
    shift
    OUT="${1:-/tmp/shots}"
    mkdir -p "$OUT"
    EXE="$(detect_exe)" || { echo "找不到可执行文件，先跑 scripts/build.sh"; exit 1; }
    export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
    exec "$EXE" --screenshot "$OUT"
    ;;
  -h|--help)
    sed -n '2,9p' "${BASH_SOURCE[0]}"
    exit 0
    ;;
  *)
    if [ -z "${DISPLAY:-}" ] && [ -z "${WAYLAND_DISPLAY:-}" ]; then
      cat <<'EOF'
没有检测到图形会话（DISPLAY / WAYLAND_DISPLAY 都为空）。

  WSL 用户：
    1) 检查 %USERPROFILE%\.wslconfig 的 [wsl2] 段，不能有 guiApplications=false
    2) 在 Windows 里执行：wsl --shutdown
    3) 重新进入 WSL 后：echo $DISPLAY 应输出 :0
    4) 再启动：run-linux.cmd

  没有图形环境可以先跑无头截图：
    scripts/run.sh --screenshot /tmp/shots
EOF
      exit 1
    fi

    EXE="$(detect_exe)" || {
      echo "找不到可执行文件。先构建："
      echo "  wsl -d Ubuntu -- bash $REPO_ROOT/scripts/build.sh"
      exit 1
    }

    # 构建输出是 framework-dependent，需要告诉 apphost 运行时在哪
    export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
    exec "$EXE" "$@"
    ;;
esac
