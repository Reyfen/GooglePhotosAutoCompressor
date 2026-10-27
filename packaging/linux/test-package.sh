#!/usr/bin/env bash
# Tests a built package on a clean Linux machine or container (used by the "Packages" workflow).
#
# Usage: test-package.sh deb|appimage|appimage-fuse <dist dir> <output dir> <name>
#   deb            install the .deb with the system package manager, run the installed command
#   appimage       unpack the .AppImage.tar.gz and run it with --appimage-extract-and-run (containers have no FUSE)
#   appimage-fuse  run the AppImage as a double-click would (needs FUSE; for real VMs)
#
# Checks: the setup check passes (ffmpeg, photos, folders; the browser too when --with-browser is given),
# and the window opens on a virtual screen. Saves a screenshot and the logs to <output dir>.
set -euo pipefail

MODE="$1"
DIST="$(cd "$2" && pwd)"
OUT="$3"
NAME="$4"
WITH_BROWSER="${5:-}"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"

SUDO=""
if [ "$(id -u)" != "0" ]; then SUDO="sudo"; fi

install_test_tools() {
  if command -v apt-get >/dev/null; then
    $SUDO apt-get update -qq
    DEBIAN_FRONTEND=noninteractive $SUDO apt-get install -y -qq xvfb xauth x11-utils imagemagick procps ca-certificates "$@" >/dev/null
  elif command -v dnf >/dev/null; then
    $SUDO dnf install -y -q xorg-x11-server-Xvfb xorg-x11-xauth xwininfo ImageMagick procps-ng which ca-certificates "$@" >/dev/null
  else
    echo "Unsupported package manager"; exit 1
  fi
}

case "$MODE" in
  deb)
    install_test_tools
    DEB="$(ls "$DIST"/*.deb | head -1)"
    echo "== Installing $DEB"
    # apt resolves the Depends list, like double-clicking the .deb in a software centre would.
    DEBIAN_FRONTEND=noninteractive $SUDO apt-get install -y -qq "$DEB" >/dev/null
    test -f /usr/share/applications/google-photos-auto-compressor.desktop
    test -f /usr/share/icons/hicolor/512x512/apps/google-photos-auto-compressor.png
    APP=(google-photos-auto-compressor)
    ;;
  appimage|appimage-fuse)
    if command -v dnf >/dev/null; then
      # What any desktop has; minimal containers do not.
      install_test_tools libX11 libICE libSM libXext libXrandr libXi libXcursor fontconfig freetype openssl-libs zlib libstdc++ ffmpeg-free
    else
      install_test_tools libx11-6 libice6 libsm6 libxext6 libxrandr2 libxi6 libxcursor1 libfontconfig1 libfreetype6 ffmpeg
    fi
    TAR="$(ls "$DIST"/*.AppImage.tar.gz | head -1)"
    echo "== Unpacking $TAR"
    mkdir -p /tmp/appimage-test && tar -xzf "$TAR" -C /tmp/appimage-test
    APPIMAGE="$(ls /tmp/appimage-test/*/*.AppImage | head -1)"
    test -x "$APPIMAGE" || { echo "AppImage lost its executable bit"; exit 1; }
    if [ "$MODE" = "appimage" ]; then
      APP=("$APPIMAGE" --appimage-extract-and-run)
    else
      APP=("$APPIMAGE")
    fi
    ;;
  *) echo "Unknown mode $MODE"; exit 1 ;;
esac

echo "== Setup check"
CHECK_ARGS=(--check)
if [ "$WITH_BROWSER" != "--with-browser" ]; then CHECK_ARGS+=(--no-browser); fi
set +e
"${APP[@]}" "${CHECK_ARGS[@]}" | tee "$OUT/$NAME-check.txt"
CHECK_EXIT=${PIPESTATUS[0]}
set -e
if [ "$CHECK_EXIT" != "0" ]; then echo "Setup check failed ($CHECK_EXIT)"; exit 1; fi

echo "== Opening the window on a virtual screen"
export APP_CMD="${APP[*]}"
export SHOT="$OUT/$NAME.png"
xvfb-run -a -s "-screen 0 1280x860x24" bash -c '
  $APP_CMD > "'"$OUT/$NAME-app.log"'" 2>&1 &
  PID=$!
  for i in $(seq 1 40); do
    sleep 1
    if ! kill -0 $PID 2>/dev/null; then echo "App exited early"; exit 1; fi
    # Wait until a window with our title is mapped (or give up after 40 s and screenshot anyway).
    if command -v xwininfo >/dev/null && xwininfo -root -tree 2>/dev/null | grep -q "Google Photos Auto Compressor"; then break; fi
  done
  sleep 3
  import -window root "$SHOT"
  kill $PID 2>/dev/null || true
  wait $PID 2>/dev/null || true
'
test -s "$SHOT"
# A blank screen compresses to almost nothing; a drawn window is much bigger.
SIZE=$(stat -c %s "$SHOT")
echo "Screenshot: $SHOT ($SIZE bytes)"
if [ "$SIZE" -lt 20000 ]; then echo "Screenshot looks empty: the window did not draw"; exit 1; fi
echo "== $NAME OK"
