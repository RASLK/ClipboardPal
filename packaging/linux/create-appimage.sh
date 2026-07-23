#!/usr/bin/env bash
# Build a portable AppImage from a published ClipboardPal binary.
# Usage: ./create-appimage.sh <binary> <version> <output.AppImage> [icon.png]
set -euo pipefail

SRC_BIN="${1:?published binary}"
VERSION="${2:?version}"
OUT_APPIMAGE="${3:?output AppImage path}"
ICON_SRC="${4:-}"

ROOT="$(cd "$(dirname "$0")" && pwd)"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

APPDIR="${STAGE}/ClipboardPal.AppDir"
mkdir -p "${APPDIR}/usr/bin" "${APPDIR}/usr/share/icons/hicolor/256x256/apps"

cp "$SRC_BIN" "${APPDIR}/usr/bin/ClipboardPal"
chmod +x "${APPDIR}/usr/bin/ClipboardPal"

if [[ -n "$ICON_SRC" && -f "$ICON_SRC" ]]; then
  cp "$ICON_SRC" "${APPDIR}/clipboardpal.png"
  cp "$ICON_SRC" "${APPDIR}/usr/share/icons/hicolor/256x256/apps/clipboardpal.png"
fi

cat > "${APPDIR}/ClipboardPal.desktop" << EOF
[Desktop Entry]
Type=Application
Name=ClipboardPal
Comment=Cross-platform clipboard history
Exec=ClipboardPal
Icon=clipboardpal
Categories=Utility;Office;
Terminal=false
StartupNotify=true
EOF

mkdir -p "${APPDIR}/usr/share/applications"
cp "${APPDIR}/ClipboardPal.desktop" "${APPDIR}/usr/share/applications/ClipboardPal.desktop"

cat > "${APPDIR}/AppRun" << 'EOF'
#!/bin/bash
HERE="$(dirname "$(readlink -f "$0")")"
export PATH="${HERE}/usr/bin:${PATH}"
exec "${HERE}/usr/bin/ClipboardPal" "$@"
EOF
chmod +x "${APPDIR}/AppRun"

ARCH_NAME="x86_64"
TOOL="${STAGE}/appimagetool-${ARCH_NAME}.AppImage"
wget -q -O "$TOOL" \
  "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-${ARCH_NAME}.AppImage"
chmod +x "$TOOL"

mkdir -p "$(dirname "$OUT_APPIMAGE")"
# Extracted mode avoids FUSE requirement on some CI runners.
"$TOOL" --appimage-extract-and-run "$APPDIR" "$OUT_APPIMAGE"
chmod +x "$OUT_APPIMAGE"
echo "Created $OUT_APPIMAGE"
ls -lh "$OUT_APPIMAGE"
