#!/usr/bin/env bash
# Builds the Linux packages on Linux:
#   dist/GooglePhotosAutoCompressor-<version>-<arch>.AppImage.tar.gz   (any distro, double-click to run)
#   dist/google-photos-auto-compressor_<version>_<debarch>.deb          (Debian, Ubuntu, Mint, ...)
#
# Usage: packaging/linux/build-packages.sh [version] [x64|arm64]
# Needs: .NET 8 SDK, dpkg-deb, curl or wget. appimagetool is downloaded when missing.
set -euo pipefail

VERSION="${1:-1.0.0}"
ARCH="${2:-x64}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
HERE="$ROOT/packaging/linux"
DIST="$ROOT/dist"
WORK="$DIST/linux-$ARCH-work"
APP_ID="google-photos-auto-compressor"
EXE="GooglePhotosAutoCompressor"

case "$ARCH" in
  x64)   RID=linux-x64;   APPIMAGE_ARCH=x86_64;  DEB_ARCH=amd64 ;;
  arm64) RID=linux-arm64; APPIMAGE_ARCH=aarch64; DEB_ARCH=arm64 ;;
  *) echo "Unknown arch $ARCH (use x64 or arm64)"; exit 1 ;;
esac

rm -rf "$WORK"
mkdir -p "$WORK" "$DIST"

echo "== Publishing $RID $VERSION"
dotnet publish "$ROOT/GooglePhotosAutoCompressor.App" -c Release -r "$RID" --self-contained \
  -p:Version="$VERSION" -p:DebugType=none -o "$WORK/publish"
chmod +x "$WORK/publish/$EXE"
# The Playwright driver ships its own node binary; make sure it stays executable.
find "$WORK/publish/.playwright" -type f \( -name node -o -name '*.sh' \) -exec chmod +x {} + 2>/dev/null || true

# ---------- AppImage ----------
echo "== Building AppImage"
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

TOOL="${APPIMAGETOOL:-$WORK/appimagetool}"
if [ ! -x "$TOOL" ]; then
  HOST_ARCH="$(uname -m)"
  URL="https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$HOST_ARCH.AppImage"
  echo "   downloading $URL"
  if command -v curl >/dev/null; then curl -fsSL "$URL" -o "$TOOL"; else wget -q "$URL" -O "$TOOL"; fi
  chmod +x "$TOOL"
fi

APPIMAGE="$WORK/$EXE-$VERSION-$APPIMAGE_ARCH.AppImage"
# Extract-and-run: works in containers and CI without FUSE.
ARCH="$APPIMAGE_ARCH" APPIMAGE_EXTRACT_AND_RUN=1 "$TOOL" --no-appstream "$APPDIR" "$APPIMAGE"
chmod +x "$APPIMAGE"

# tar keeps the executable bit, so the AppImage runs on double-click right after unpacking.
TARDIR="$WORK/tar/$EXE-$VERSION"
mkdir -p "$TARDIR"
cp "$APPIMAGE" "$TARDIR/"
cp "$HERE/README-AppImage.txt" "$TARDIR/README.txt"
tar -C "$WORK/tar" -czf "$DIST/$EXE-$VERSION-$APPIMAGE_ARCH.AppImage.tar.gz" "$EXE-$VERSION"

# ---------- .deb ----------
echo "== Building .deb"
DEBROOT="$WORK/deb"
mkdir -p "$DEBROOT/DEBIAN" "$DEBROOT/opt/$APP_ID" "$DEBROOT/usr/bin" "$DEBROOT/usr/share/applications" \
         "$DEBROOT/usr/share/icons/hicolor/512x512/apps" "$DEBROOT/usr/share/doc/$APP_ID"
cp -a "$WORK/publish/." "$DEBROOT/opt/$APP_ID/"
ln -sf "/opt/$APP_ID/$EXE" "$DEBROOT/usr/bin/$APP_ID"
cp "$HERE/$APP_ID.desktop" "$DEBROOT/usr/share/applications/"
cp "$HERE/icon-512.png" "$DEBROOT/usr/share/icons/hicolor/512x512/apps/$APP_ID.png"
cp "$ROOT/LICENSE" "$DEBROOT/usr/share/doc/$APP_ID/copyright"
INSTALLED_KB="$(du -sk "$DEBROOT" | cut -f1)"
sed -e "s/@VERSION@/$VERSION/" -e "s/@ARCH@/$DEB_ARCH/" -e "s/@SIZE@/$INSTALLED_KB/" "$HERE/control" > "$DEBROOT/DEBIAN/control"
cp "$HERE/postinst" "$HERE/postrm" "$DEBROOT/DEBIAN/"
chmod 755 "$DEBROOT/DEBIAN/postinst" "$DEBROOT/DEBIAN/postrm"
find "$DEBROOT/opt" -type d -exec chmod 755 {} +
dpkg-deb --root-owner-group --build "$DEBROOT" "$DIST/${APP_ID}_${VERSION}_${DEB_ARCH}.deb"

rm -rf "$WORK"
echo "== Done"
ls -lh "$DIST"
