#!/usr/bin/env bash
# Builds PdfEdit Desktop (the Avalonia app) for Linux, 64-bit Intel/AMD:
#   dist/PdfEdit-Desktop-<version>-linux-x64.AppImage   one file: make it executable and run it
#   dist/PdfEdit-Desktop-<version>-linux-x64.tar.gz     unpack anywhere and run ./PdfEdit
#
#   installer/desktop/build-linux.sh           # version from Directory.Build.props
#   installer/desktop/build-linux.sh 1.4.2
#
# .NET is included. The page is shown with the system's WebKitGTK (webkit2gtk 4.1 or 4.0), which
# desktop Linux usually has; it isn't bundled. The AppImage is made with appimagetool, downloaded
# here when it isn't on the PATH.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
HERE="$ROOT/installer/desktop"
CSPROJ="$ROOT/PdfEdit.Avalonia/PdfEdit.Avalonia.csproj"
VERSION="${1:-}"
if [[ -z "$VERSION" ]]; then
  VERSION="$(grep -m1 -o '<Version>[^<]*' "$ROOT/Directory.Build.props" | sed 's/<Version>//')"
  VERSION="${VERSION:-1.0.0}"
fi
DIST="$ROOT/dist"
WORK="$ROOT/obj-desktop-linux"
PUB="$WORK/publish"
APPDIR="$WORK/PdfEdit.AppDir"
NAME="PdfEdit-Desktop-$VERSION-linux-x64"
APPIMAGETOOL_URL="https://github.com/AppImage/appimagetool/releases/download/1.9.0/appimagetool-x86_64.AppImage"
echo "=== PdfEdit Desktop for Linux  v$VERSION ==="

rm -rf "$WORK"; mkdir -p "$WORK" "$DIST"
dotnet publish "$CSPROJ" -c Release -r linux-x64 --self-contained true -o "$PUB" \
  -p:Version="$VERSION" -p:DebugType=none -p:DebugSymbols=false
[[ -f "$PUB/PdfEdit" && -d "$PUB/wwwroot" ]] || { echo "publish is missing PdfEdit or wwwroot"; exit 1; }
chmod +x "$PUB/PdfEdit"
cp "$HERE/pdfedit.desktop" "$PUB/pdfedit.desktop"
cp "$ROOT/PdfEdit.Avalonia/Assets/pdfedit.png" "$PUB/pdfedit.png"

# The tarball: a PdfEdit-<version> folder holding the program and everything it loads.
rm -f "$DIST/$NAME.tar.gz"
mkdir -p "$WORK/tar"; cp -R "$PUB" "$WORK/tar/PdfEdit-$VERSION"
tar -C "$WORK/tar" -czf "$DIST/$NAME.tar.gz" "PdfEdit-$VERSION"
echo "Built $DIST/$NAME.tar.gz"

# The AppImage: the same files in usr/bin, started by AppRun (PdfEdit finds its page files next to itself).
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps"
cp -R "$PUB/." "$APPDIR/usr/bin/"
cp "$HERE/pdfedit.desktop" "$APPDIR/pdfedit.desktop"
cp "$HERE/pdfedit.desktop" "$APPDIR/usr/share/applications/pdfedit.desktop"
cp "$ROOT/PdfEdit.Avalonia/Assets/pdfedit.png" "$APPDIR/pdfedit.png"
cp "$ROOT/PdfEdit.Avalonia/Assets/pdfedit.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/pdfedit.png"
ln -s pdfedit.png "$APPDIR/.DirIcon"
cat > "$APPDIR/AppRun" <<'RUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/PdfEdit" "$@"
RUN
chmod +x "$APPDIR/AppRun"

TOOL="$(command -v appimagetool || true)"
if [[ -z "$TOOL" ]]; then
  TOOL="$WORK/appimagetool"
  curl -fsSL -o "$TOOL" "$APPIMAGETOOL_URL"
  chmod +x "$TOOL"
fi
rm -f "$DIST/$NAME.AppImage"
# Extract-and-run: build machines (and containers) often have no FUSE.
APPIMAGE_EXTRACT_AND_RUN=1 ARCH=x86_64 VERSION="$VERSION" "$TOOL" --no-appstream "$APPDIR" "$DIST/$NAME.AppImage"
chmod +x "$DIST/$NAME.AppImage"
echo "Built $DIST/$NAME.AppImage"
