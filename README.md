# GooglePhotosAutoCompressor
Utility to reduce video files size on your google photo, so you don't need to pay even more to corporates

It drives a normal Chrome window (via Playwright) on your own Google Photos account:

1. **Download** large items from *Storage → Large files*: videos above 0.8 MB per second, and photos above 2 MB.
2. **Compress** them:
   - Videos with ffmpeg: half resolution, H.264 CRF 20, audio and metadata kept.
   - JPEG photos with Magick.NET: at most 16 MP, quality 85. EXIF data (date taken, location, camera, orientation) and the colour profile are kept.
   - HEIC, PNG and photos without a date taken are left alone.
3. **Check** each copy. A video must be as long as the original; a photo must keep its proportions, orientation and date taken. Both must be at least 10% smaller. Otherwise the original is left alone.
4. **Move originals to the bin.** This happens before upload on purpose, so it works when storage is full. Google keeps the bin for 60 days.
5. **Upload** the compressed copies.
6. **Set the date** of each video copy back to the original date. Photo copies already carry their date taken, so they skip this step.

Progress is saved to `~/Downloads/GooglePhotosCache.json` after every step, so you can stop and re-run at any time. Each run handles up to 10 videos (`Const.TOTAL_FILES_TO_PROCESS_IN_ONE_BATCH`).

## Requirements
- Windows or Linux (x64 or arm64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Google Chrome (recommended). Without it the app installs Playwright's Chromium, but Google sign-in may refuse it.
- ffmpeg. Windows builds include it (needs Git LFS). Otherwise the app uses `ffmpeg` from PATH, or offers to download a verified build from [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds).

## Run
```
dotnet run --project GooglePhotosAutoCompressor
```
On first start, sign in to Google in the browser window. The session is kept in the browser profile folder for later runs.

Linux may also need the browser's system libraries:
```
pwsh GooglePhotosAutoCompressor/bin/Debug/net8.0/playwright.ps1 install-deps chromium
```

## Tests
```
dotnet test
```
Tests that need ffmpeg skip themselves when it is not installed. The real ffmpeg download test runs only with `GPAC_TEST_FFMPEG_DOWNLOAD=1`.

### Live Google Photos page tests (AI checked)
`GooglePhotosAutoCompressor.Tests/Live` opens the real Google Photos website with the app's signed-in browser profile and walks through what every step relies on. It uses the same selectors and clicks as the app:

| Test | Step it protects |
|------|------------------|
| Home page signed in, UI in English (UK) | all (selectors rely on English labels) |
| Large files scan reads video tiles (size, date, length) | download, delete |
| Clicking a tile selects it and shows Download and Move to bin | download, delete |
| Download starts a single-file download (then cancelled) | download |
| Create → Import from computer opens a file picker (no file given) | upload |
| Viewer shows file name and date, "next" moves on | date |
| Date dialog has year, month and day fields; Escape closes it unchanged | date |
| Move to bin asks for confirmation (off unless `GPAC_TEST_LIVE_BIN_DIALOG=1`) | delete |

After each check, your local [Claude Code](https://claude.com/claude-code) CLI looks at a screenshot and confirms the page looks right. When a selector or click fails, Claude Code describes what the page shows instead, so the failure tells you what Google changed. Screenshots are kept in `%TEMP%/gpac-live-screens` (`/tmp/gpac-live-screens` on Linux).

- **Nothing in your account is changed:** no upload, no delete, no date saved.
- **Needs:** Google Chrome, `claude` installed and signed in, and a signed-in profile. If something is missing, the tests are skipped and the output says what to do. To sign in during a test run, set `GPAC_TEST_SIGN_IN=1` and sign in within 15 minutes in the browser window the tests open.
- **Privacy:** screenshots of your library (thumbnails, one opened item) are sent to Claude. A separate test Google account with a few videos is recommended. Point the tests at its profile with `GPAC_TEST_PROFILE_DIR`.
- **Cost and time:** about 1–2 minutes and some Claude usage per run.
- **Leave them out:** `dotnet test --filter "Category!=Live"`. To turn them off for every run, set `GPAC_TEST_LIVE=0`.
- **Other settings:** `GPAC_TEST_CLAUDE_MODEL` (default `sonnet`), `GPAC_TEST_CLAUDE_PATH`, `GPAC_TEST_HEADLESS=1`.

## Caveats
- It works by clicking through the Google Photos website, so a change to that site can break it. All selectors are in `GooglePhotosAutoCompressor/Selectors.cs`.
- Automating the Google Photos website may be against Google's terms of service. Use it on your own account, at your own risk.
- Compression is lossy and cannot be undone after the bin is emptied.
