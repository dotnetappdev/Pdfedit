#!/usr/bin/env bash
# Builds PdfEdit Desktop (the Avalonia app) for macOS: dist/PdfEdit-Desktop-<version>-mac-<arch>.dmg
# holding PdfEdit.app, for Apple silicon (arm64) and Intel (x64).
#
#   installer/desktop/build-mac.sh                 # both chips, version from the csproj
#   installer/desktop/build-mac.sh 1.3.1 arm64     # one chip, a given version
#
# Signing (optional — set these to sign and notarize; otherwise the app is signed ad hoc, which runs
# after the user right-clicks it and chooses Open the first time):
#   MACOS_SIGN_IDENTITY   "Developer ID Application: Your Name (TEAMID)" (in the keychain)
#   APPLE_ID, APPLE_TEAM_ID, APPLE_APP_PASSWORD   for notarization with notarytool
#
# On a Mac this makes .dmg files; elsewhere (to check the bundle) it makes .tar.gz files instead.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
HERE="$ROOT/installer/desktop"
CSPROJ="$ROOT/PdfEdit.Avalonia/PdfEdit.Avalonia.csproj"
VERSION="${1:-}"
ARCHES="${2:-arm64 x64}"
if [[ -z "$VERSION" ]]; then
  VERSION="$(grep -m1 -o '<Version>[^<]*' "$CSPROJ" | sed 's/<Version>//')"
  VERSION="${VERSION:-1.0.0}"
fi
DIST="$ROOT/dist"
WORK="$ROOT/obj-desktop-mac"
mkdir -p "$DIST"
ON_MAC=false; [[ "$(uname)" == "Darwin" ]] && ON_MAC=true
echo "=== PdfEdit Desktop for macOS  v$VERSION  ($ARCHES) ==="

sign() {   # sign one file or bundle
  if [[ -n "${MACOS_SIGN_IDENTITY:-}" ]]; then
    codesign --force --timestamp --options runtime --entitlements "$HERE/PdfEdit.entitlements" -s "$MACOS_SIGN_IDENTITY" "$1"
  else
    codesign --force -s - "$1"
  fi
}

for ARCH in $ARCHES; do
  PUB="$WORK/publish-$ARCH"
  APP="$WORK/$ARCH/PdfEdit.app"
  rm -rf "$PUB" "$WORK/$ARCH"
  dotnet publish "$CSPROJ" -c Release -r "osx-$ARCH" --self-contained true -o "$PUB" \
    -p:Version="$VERSION" -p:UseAppHost=true -p:DebugType=none -p:DebugSymbols=false
  [[ -f "$PUB/PdfEdit" && -d "$PUB/wwwroot" ]] || { echo "publish is missing PdfEdit or wwwroot"; exit 1; }

  # The .app bundle: the program and everything it loads in Contents/MacOS (PdfEdit finds its page
  # files next to itself), the icon and Info.plist alongside.
  mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
  cp -R "$PUB/." "$APP/Contents/MacOS/"
  cp "$ROOT/PdfEdit.Avalonia/Assets/pdfedit.icns" "$APP/Contents/Resources/PdfEdit.icns"
  sed "s/__VERSION__/$VERSION/g" "$HERE/Info.plist" > "$APP/Contents/Info.plist"
  printf 'APPL????' > "$APP/Contents/PkgInfo"
  chmod +x "$APP/Contents/MacOS/PdfEdit"

  if $ON_MAC; then
    # Everything in Contents/MacOS counts as code to macOS — the .NET assemblies and the web app's
    # files too — so each file is signed, then the program, then the bundle that seals them.
    find "$APP/Contents/MacOS" -type f ! -path "$APP/Contents/MacOS/PdfEdit" -print0 | while IFS= read -r -d '' f; do sign "$f" >/dev/null; done
    sign "$APP/Contents/MacOS/PdfEdit"
    sign "$APP"
    codesign --verify --deep --strict "$APP"

    DMG="$DIST/PdfEdit-Desktop-$VERSION-mac-$ARCH.dmg"
    STAGE="$WORK/$ARCH/dmg"
    rm -rf "$STAGE" "$DMG"; mkdir -p "$STAGE"
    cp -R "$APP" "$STAGE/"
    ln -s /Applications "$STAGE/Applications"
    hdiutil create -volname "PdfEdit" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
    if [[ -n "${MACOS_SIGN_IDENTITY:-}" ]]; then codesign --force --timestamp -s "$MACOS_SIGN_IDENTITY" "$DMG"; fi

    if [[ -n "${MACOS_SIGN_IDENTITY:-}" && -n "${APPLE_ID:-}" && -n "${APPLE_TEAM_ID:-}" && -n "${APPLE_APP_PASSWORD:-}" ]]; then
      xcrun notarytool submit "$DMG" --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_PASSWORD" --wait
      xcrun stapler staple "$DMG"
    fi
    echo "Built $DMG"
  else
    OUT="$DIST/PdfEdit-Desktop-$VERSION-mac-$ARCH.app.tar.gz"
    tar -C "$WORK/$ARCH" -czf "$OUT" PdfEdit.app
    echo "Not on a Mac: built $OUT (sign and package it on a Mac for a .dmg)"
  fi
done
