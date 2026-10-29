#!/usr/bin/env bash
# Packs the Linux packages:
#   dist/GooglePhotosAutoCompressor-v<version>-<arch>.AppImage.tar.gz   (any distro, double-click to run)
#   dist/google-photos-auto-compressor_v<version>_<debarch>.deb          (Debian, Ubuntu, Mint, ...)
#
# Usage: packaging/linux/build-packages.sh [v1.0.0] [x64|arm64] [published folder]
#   ("1.0.0" works too). With a published folder (dotnet publish -r linux-x64 --self-contained, e.g.
#   made on Windows by build.ps1), that is packed; without one, the app is published here first
#   (needs the .NET 8 SDK). Always needs dpkg-deb and curl or wget; appimagetool is downloaded once.
#
# Runs on Linux or in WSL (build.ps1 calls it there). The packages are put together on the Linux
# filesystem: a Windows drive (/mnt/c) cannot keep the executable bit the AppImage needs.
# Prints one line per step; tool output goes to dist/logs/linux-packages.log, shown only on failure.
set -euo pipefail

# File names use "v1.0.0"; the app and the .deb's own Version field must start with a digit ("1.0.0").
NUMBER="${1:-v1.0.0}"
NUMBER="${NUMBER#[vV]}"
VERSION="v$NUMBER"
ARCH="${2:-x64}"
PUBLISHED="${3:-}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
HERE="$ROOT/packaging/linux"
DIST="$ROOT/dist"
LOG="$DIST/logs/linux-packages.log"
APP_ID="google-photos-auto-compressor"
EXE="GooglePhotosAutoCompressor"

case "$ARCH" in
  x64)   RID=linux-x64;   APPIMAGE_ARCH=x86_64;  DEB_ARCH=amd64 ;;
  arm64) RID=linux-arm64; APPIMAGE_ARCH=aarch64; DEB_ARCH=arm64 ;;
  *) echo "Unknown arch $ARCH (use x64 or arm64)"; exit 1 ;;
esac

mkdir -p "$DIST/logs"
: > "$LOG"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# Runs a command with its output in the log; on failure shows the end of the log and stops.
quiet() {
  if ! "$@" >> "$LOG" 2>&1; then
    echo "   FAILED: $*"
    tail -n 30 "$LOG" | sed 's/^/   | /'
    echo "   Full log: $LOG"
    exit 1
  fi
}

if [ -n "$PUBLISHED" ]; then
  case "$PUBLISHED" in /*) ;; *) PUBLISHED="$ROOT/$PUBLISHED" ;; esac
  [ -f "$PUBLISHED/$EXE" ] || { echo "   No $EXE in $PUBLISHED: publish for $RID first."; exit 1; }
  mkdir -p "$WORK/publish"
  cp -a "$PUBLISHED/." "$WORK/publish/"
else
  echo "   Publishing $RID $VERSION"
  quiet dotnet publish "$ROOT/GooglePhotosAutoCompressor.App" -c Release -r "$RID" --self-contained \
    -p:Version="$NUMBER" -p:DebugType=none -v quiet -nologo -o "$WORK/publish"
fi
# Files from a Windows build have no executable bits.
chmod +x "$WORK/publish/$EXE"
# The Playwright driver ships its own node binary; make sure it stays executable.
find "$WORK/publish/.playwright" -type f \( -name node -o -name '*.sh' \) -exec chmod +x {} + 2>/dev/null || true

# ---------- AppImage ----------
echo "   AppImage"
APPDIR="$WORK/AppDir"
mkdir -p "$APPDIR/usr/lib/$APP_ID" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/512x512/apps"
cp -a "$WORK/publish/." "$APPDIR/usr/lib/$APP_ID/"
cp "$HERE/AppRun" "$APPDIR/AppRun"
chmod +x "$APPDIR/AppRun"
sed "s/^Exec=.*/Exec=$EXE/" "$HERE/$APP_ID.desktop" > "$APPDIR/$APP_ID.desktop"
cp "$APPDIR/$APP_ID.desktop" "$APPDIR/usr/share/applications/"
cp "$HERE/icon-512.png" "$APPDIR/$APP_ID.png"
cp "$HERE/icon-512.png" "$APPDIR/usr/share/icons/hicolor/512x512/apps/$APP_ID.png"
ln -sf "$APP_ID.png" "$APPDIR/.DirIcon"

# Kept between runs on the Linux side, so it is downloaded only once.
TOOL="${APPIMAGETOOL:-$HOME/.cache/gpac/appimagetool}"
if [ ! -x "$TOOL" ]; then
  mkdir -p "$(dirname "$TOOL")"
  URL="https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$(uname -m).AppImage"
  echo "   Downloading appimagetool (once)"
  if command -v curl >/dev/null; then quiet curl -fsSL "$URL" -o "$TOOL.part"; else quiet wget -q "$URL" -O "$TOOL.part"; fi
  chmod +x "$TOOL.part"
  mv "$TOOL.part" "$TOOL"
fi

APPIMAGE="$WORK/$EXE-$VERSION-$APPIMAGE_ARCH.AppImage"
# Extract-and-run: works in containers, CI and WSL without FUSE.
ARCH="$APPIMAGE_ARCH" APPIMAGE_EXTRACT_AND_RUN=1 quiet "$TOOL" --no-appstream "$APPDIR" "$APPIMAGE"
chmod +x "$APPIMAGE"

# tar keeps the executable bit, so the AppImage runs on double-click right after unpacking.
TARDIR="$WORK/tar/$EXE-$VERSION"
mkdir -p "$TARDIR"
cp "$APPIMAGE" "$TARDIR/"
cp "$HERE/README-AppImage.txt" "$TARDIR/README.txt"
tar -C "$WORK/tar" -czf "$WORK/out.tar.gz" "$EXE-$VERSION"
mv "$WORK/out.tar.gz" "$DIST/$EXE-$VERSION-$APPIMAGE_ARCH.AppImage.tar.gz"

# ---------- .deb ----------
echo "   .deb"
DEBROOT="$WORK/deb"
mkdir -p "$DEBROOT/DEBIAN" "$DEBROOT/opt/$APP_ID" "$DEBROOT/usr/bin" "$DEBROOT/usr/share/applications" \
         "$DEBROOT/usr/share/icons/hicolor/512x512/apps" "$DEBROOT/usr/share/doc/$APP_ID"
cp -a "$WORK/publish/." "$DEBROOT/opt/$APP_ID/"
ln -sf "/opt/$APP_ID/$EXE" "$DEBROOT/usr/bin/$APP_ID"
cp "$HERE/$APP_ID.desktop" "$DEBROOT/usr/share/applications/"
cp "$HERE/icon-512.png" "$DEBROOT/usr/share/icons/hicolor/512x512/apps/$APP_ID.png"
cp "$ROOT/LICENSE" "$DEBROOT/usr/share/doc/$APP_ID/copyright"
INSTALLED_KB="$(du -sk "$DEBROOT" | cut -f1)"
sed -e "s/@VERSION@/$NUMBER/" -e "s/@ARCH@/$DEB_ARCH/" -e "s/@SIZE@/$INSTALLED_KB/" "$HERE/control" > "$DEBROOT/DEBIAN/control"
cp "$HERE/postinst" "$HERE/postrm" "$DEBROOT/DEBIAN/"
chmod 755 "$DEBROOT/DEBIAN/postinst" "$DEBROOT/DEBIAN/postrm"
find "$DEBROOT/opt" -type d -exec chmod 755 {} +
quiet dpkg-deb --root-owner-group --build "$DEBROOT" "$WORK/out.deb"
mv "$WORK/out.deb" "$DIST/${APP_ID}_${VERSION}_${DEB_ARCH}.deb"
