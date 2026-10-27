# GooglePhotosAutoCompressor
Utility to reduce video files size on your google photo, so you don't need to pay even more to corporates

It drives a normal Chrome window (via Playwright) on your own Google Photos account:

1. **Download** large videos from *Storage → Large files* (only ones above 0.8 MB per second of video).
2. **Compress** them with ffmpeg: half resolution, H.264 CRF 20, audio and metadata kept.
3. **Check** each copy: same length as the original and at least 10% smaller. Otherwise the original is left alone.
4. **Move originals to the bin.** This happens before upload on purpose, so it works when storage is full. Google keeps the bin for 60 days.
5. **Upload** the compressed copies.
6. **Set the date** of each copy back to the original date.

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

### AI image tests
`CompressorVisionTests` compresses generated clips and asks your local [Claude Code](https://claude.com/claude-code) CLI to look at a frame from the original and from the compressed copy: is it the same picture, and is it damaged (blocks, smearing, wrong colours, rotated or stretched)? Two control tests check that it does catch a damaged frame and a different picture.

- They need `claude` installed and signed in. If it is missing, the tests are skipped and the test output says what to install.
- If it is installed but not signed in, they fail and say to sign in.
- They take about 10 s each and use your Claude plan or API credit (a few cents per run).
- Skip them with `dotnet test --filter "Category!=AI"`.
- Settings: `GPAC_TEST_CLAUDE_MODEL` (default `sonnet`), `GPAC_TEST_CLAUDE_PATH` (path to `claude` if it is not on PATH).

## Caveats
- It works by clicking through the Google Photos website, so a change to that site can break it. All selectors are in `GooglePhotosAutoCompressor/Selectors.cs`.
- Automating the Google Photos website may be against Google's terms of service. Use it on your own account, at your own risk.
- Compression is lossy and cannot be undone after the bin is emptied.
