Google Photos Auto Compressor
=============================

Double-click the .AppImage file to start it. Nothing needs to be installed.

If double-clicking does nothing:
  - Right-click the file > Properties > Permissions > "Allow executing file as program", or run:
        chmod +x GooglePhotosAutoCompressor-*.AppImage
  - Some very old or minimal systems lack FUSE. Run it like this instead:
        ./GooglePhotosAutoCompressor-*.AppImage --appimage-extract-and-run

The app also needs:
  - Google Chrome (recommended; otherwise it installs Chromium on first start)
  - ffmpeg (if it is not installed, the app offers to download it)

Check everything without opening Google Photos:
        ./GooglePhotosAutoCompressor-*.AppImage --check

More: https://github.com/Reyfen/GooglePhotosAutoCompressor
