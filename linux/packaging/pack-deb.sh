#!/usr/bin/env bash
# 打 .deb 包（在 WSL / Linux 里运行）
#
# 用法：packaging/pack-deb.sh [版本号]
# 产物：<artifacts>/deb/minesweeper2_<版本>_amd64.deb
#
# 依赖：dpkg-deb、strip（binutils）
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
ARTIFACTS="${ARTIFACTS:-$HOME/artifacts/ms2linux}"
VERSION="${1:-1.5.2}"
ARCH="${ARCH:-amd64}"
RID="${RID:-linux-x64}"
PKGNAME="minesweeper2"

PROJ="$REPO_ROOT/src/MineSweeper.App.Linux/MineSweeper.App.Linux.csproj"
WORK="$ARTIFACTS/deb"
PUBLISH="$WORK/publish"
PKG="$WORK/pkg"
DEB="$WORK/${PKGNAME}_${VERSION}_${ARCH}.deb"

echo "== 1/6 publish（self-contained $RID）=="
rm -rf "$PUBLISH" "$PKG"
"$DOTNET" publish "$PROJ" \
  -c Release -r "$RID" --self-contained true \
  -p:DebugType=None -p:DebugSymbols=false \
  -o "$PUBLISH" -v minimal 2>&1 | tail -3

echo
echo "== 2/6 组装包目录 =="
mkdir -p "$PKG/DEBIAN" \
         "$PKG/usr/lib/$PKGNAME" \
         "$PKG/usr/bin" \
         "$PKG/usr/share/applications" \
         "$PKG/usr/share/icons/hicolor/256x256/apps" \
         "$PKG/usr/share/icons/hicolor/512x512/apps" \
         "$PKG/usr/share/metainfo" \
         "$PKG/usr/share/doc/$PKGNAME" \
         "$PKG/usr/share/lintian/overrides"

cp -a "$PUBLISH/." "$PKG/usr/lib/$PKGNAME/"
ln -sf "../lib/$PKGNAME/Minesweeper2" "$PKG/usr/bin/$PKGNAME"

# --- 权限：lintian 要求托管 dll / .so / 数据文件一律 644，可执行只能 755 ---
find "$PKG/usr/lib/$PKGNAME" -maxdepth 1 -type f \
  \( -name '*.dll' -o -name '*.so' -o -name '*.json' -o -name '*.xml' \) -exec chmod 644 {} +
chmod 755 "$PKG/usr/lib/$PKGNAME/Minesweeper2"
[ -f "$PKG/usr/lib/$PKGNAME/createdump" ] && chmod 755 "$PKG/usr/lib/$PKGNAME/createdump" || true

# --- 裁剪 SkiaSharp / HarfBuzz 的调试符号（发布版未 strip）---
for so in "$PKG/usr/lib/$PKGNAME"/libSkiaSharp.so "$PKG/usr/lib/$PKGNAME"/libHarfBuzzSharp.so; do
  [ -f "$so" ] && strip --strip-unneeded "$so" 2>/dev/null || true
done

cp "$REPO_ROOT/packaging/minesweeper2.desktop" "$PKG/usr/share/applications/"
cp "$REPO_ROOT/packaging/minesweeper2.metainfo.xml" "$PKG/usr/share/metainfo/"

# 图标：复用 Windows 仓库里的自绘图标（只读，不修改）；256/512 都是正方形
WIN="$REPO_ROOT/../MineSweeper2.0-Windows"
cp "$WIN/docs/assets/img/icon-256.png" "$PKG/usr/share/icons/hicolor/256x256/apps/$PKGNAME.png"
cp "$WIN/docs/assets/img/icon-512.png" "$PKG/usr/share/icons/hicolor/512x512/apps/$PKGNAME.png"

# lintian override：.NET / SkiaSharp 运行时自带第三方库是 self-contained 的固有形态
cat > "$PKG/usr/share/lintian/overrides/$PKGNAME" <<'OVR'
# .NET self-contained 运行时与 SkiaSharp 自带 zlib/libpng/libjpeg/freetype；
# 拆成系统依赖会破坏可移植性，故保留并 override。
minesweeper2 binary: embedded-library *
# 游戏逻辑在自绘棋盘里，lintian 的启发式认不出来。
minesweeper2 binary: package-section-games-but-contains-no-game
# 技术预览版暂不提供 man page。
minesweeper2 binary: no-manual-page
# lintian 的 validate-tree 在"未安装的包目录"上会误报；单独 appstreamcli validate 已通过。
minesweeper2 binary: appstream-metadata-validation-failed *
OVR

# usr/share 下的一切必须是 644（否则 drvfs 的 777 权限会被带进包里）
find "$PKG/usr/share" -type f -exec chmod 644 {} +

echo
echo "== 3/6 control / 文档 =="
INSTALLED_KB=$(du -sk "$PKG" | cut -f1)
cat > "$PKG/DEBIAN/control" <<CTL
Package: $PKGNAME
Version: $VERSION
Architecture: $ARCH
Maintainer: Fastrof <noreply@example.com>
Installed-Size: $INSTALLED_KB
Depends: libc6 (>= 2.35), libstdc++6, libgcc-s1, zlib1g, libx11-6, libice6, libsm6, libfontconfig1, libfreetype6, fonts-dejavu-core
Recommends: libssl3t64 | libssl3
Section: games
Priority: optional
Homepage: https://github.com/Fastrof/Minesweeper2.0
Description: Minesweeper 2.0 - classic minesweeper with levels and items
 A hand-written minesweeper: classic rules first (safe first click,
 chording, flag/question cycle), then 36 levels, three items, a coin shop,
 31 achievements, themes, bilingual UI and synthesized sound effects.
 .
 This is the Linux (Avalonia) build; the Windows build is distributed
 separately.
CTL

cat > "$PKG/usr/share/doc/$PKGNAME/copyright" <<'CPY'
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: Minesweeper2.0
Upstream-Contact: Fastrof <https://github.com/Fastrof/Minesweeper2.0>

Files: *
Copyright: 2026 Fastrof & Shinichi
License: Proprietary
 For learning and exchange only (see the upstream LICENSE file).

Files: usr/lib/minesweeper2/*
Copyright: .NET Foundation, Avalonia contributors, Microsoft, SkiaSharp contributors
License: MIT
 Bundled runtime and framework dependencies.
CPY

cat > "$PKG/usr/share/doc/$PKGNAME/changelog" <<CHG
$PKGNAME ($VERSION) unstable; urgency=medium

  * First Linux (Avalonia) technical preview: classic game, self-drawn board
    with far-zoom row-band merging, headless screenshot mode for CI.

 -- Fastrof <noreply@example.com>  Wed, 21 Oct 2026 12:00:00 +0800
CHG
gzip -9n "$PKG/usr/share/doc/$PKGNAME/changelog"

echo
echo "== 4/6 md5sums =="
( cd "$PKG" && find . -path ./DEBIAN -prune -o -type f -print | sed 's|^\./||' \
  | while read -r f; do printf '%s  %s\n' "$(md5sum "$f" | cut -d' ' -f1)" "$f"; done > DEBIAN/md5sums )

echo
echo "== 5/6 dpkg-deb =="
dpkg-deb --build --root-owner-group "$PKG" "$DEB"

echo
echo "== 6/6 校验 =="
echo "=== 产物 ==="
ls -lh "$DEB" | awk '{print $5, $9}'
echo
echo "=== 包信息 ==="
dpkg-deb -f "$DEB" | head -12
echo
echo "=== lintian ==="
if command -v lintian >/dev/null 2>&1; then
  lintian "$DEB" 2>&1 | head -30 || true
  echo "（剩余告警见上；E 级已用 override 处理 embedded-library 一类）"
else
  echo "lintian 未安装，跳过"
fi
echo
echo "=== appstream 校验 ==="
if command -v appstreamcli >/dev/null 2>&1; then
  appstreamcli validate --no-net "$PKG/usr/share/metainfo/minesweeper2.metainfo.xml" 2>&1 | head -12
else
  echo "appstreamcli 未安装，跳过"
fi
