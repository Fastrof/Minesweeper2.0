#!/usr/bin/env bash
# 构建 Linux 版（在 WSL / Linux 里运行）
#
# 隔离保证：所有 obj/bin 输出到 Linux 侧 $ARTIFACTS（默认 ~/artifacts/ms2linux），
#          Windows 仓库目录 MineSweeper2.0-Windows 一个文件都不会被写入。
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
ARTIFACTS="${ARTIFACTS:-$HOME/artifacts/ms2linux}"
PROJ="$REPO_ROOT/src/MineSweeper.App.Linux/MineSweeper.App.Linux.csproj"
CONFIG="${CONFIG:-Release}"

echo "== build =="
echo "repo     : $REPO_ROOT"
echo "dotnet   : $DOTNET ($($DOTNET --version))"
echo "artifacts: $ARTIFACTS"
echo "config   : $CONFIG"

"$DOTNET" build "$PROJ" -c "$CONFIG" --artifacts-path "$ARTIFACTS" -v minimal "$@"

echo
echo "== 产物 =="
find "$ARTIFACTS/bin" -maxdepth 3 -type d 2>/dev/null | head -10
