# GooglePhotosAutoCompressor

Frees up Google Photos storage by replacing your biggest videos and photos with smaller copies.

It opens a normal Chrome window on your own Google Photos account and, for each large item:

1. **Downloads** it from *Storage → Large photos and videos*.
2. **Compresses** it. Videos go to half resolution; JPEG photos go to at most 16 megapixels. Date taken, location and other details are kept.
3. **Checks** the copy. If anything looks wrong, or it would not save at least 10%, the original is left alone.
4. **Moves the original to the bin.** Google keeps it there for 60 days, so you can still restore it.
5. **Uploads** the smaller copy.
6. **Fixes the date** of video copies so they appear in the right place in your timeline.

You can stop it at any time. Progress is saved to `~/Downloads/GooglePhotosCache.json`, and the next run carries on from there. Each run handles up to 10 items.

**Before you start:** compression loses some quality, and once the bin is emptied the originals are gone for good. The app clicks through the Google Photos website, which may be against Google's terms, and a change to that site can break it. Use it on your own account at your own risk.

## What you need

- Windows or Linux
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Google Chrome
- ffmpeg: Windows builds include it. Otherwise the app uses the one installed on your PC, or offers to download it.

## Run it

```
dotnet run --project GooglePhotosAutoCompressor
```

The first time, sign in to Google in the Chrome window that opens. You stay signed in for later runs.

## Build and test

| Script | What it does | Needs |
|--------|--------------|-------|
| `./build.ps1` | Builds the app and runs the unit tests. | .NET 8 |
| `./integration_tests.ps1` | Tries every step on the real Google Photos website and lets Claude Code check the screenshots. | Chrome, [Claude Code](https://claude.com/claude-code), a signed-in Google account |

On Linux, run them with `pwsh` (for example `pwsh ./build.ps1`).

`build.ps1` options: `-Configuration Release`, `-SkipTests`.

### Integration tests

These catch it early when Google changes its website and a step of the app stops working. Each test opens a real page, does what the app does there (scan the list, select an item, start a download, open the upload menu, open the date dialog) and stops before anything is changed. Claude Code then looks at a screenshot to confirm the page is right. When something fails, it describes what it sees instead, so you know what Google changed.

- **Your account is not changed:** nothing is uploaded, deleted or re-dated. Downloads are cancelled.
- **First time:** run `./integration_tests.ps1 -SignIn` and sign in to Google in the window that opens. You have 15 minutes.
- **Privacy:** screenshots of your library are sent to Claude. A separate test account is safer. Use it with `-ProfileDir <path to its Chrome profile>`.
- **Time and cost:** about 1–2 minutes and a little Claude usage per run.
- **Screenshots** are saved to your temp folder, under `gpac-live-screens`.
- **Other options:**
  - `-BinDialog` also opens the "Move to bin" confirmation and then cancels it. If something goes wrong, one item could end up in the bin.
  - `-Headless` runs without a window.
  - `-Model <name>` picks the Claude model (default `sonnet`).

If something is missing (Claude Code, Chrome or sign-in), the tests are skipped with a message that says what to do.

## For developers

- Plain `dotnet test` runs everything, including the integration tests. Use `--filter "Category!=Live"` to leave them out.
- Test settings (environment variables):
  - `GPAC_TEST_LIVE=0` turns the integration tests off.
  - `GPAC_TEST_SIGN_IN`, `GPAC_TEST_LIVE_BIN_DIALOG`, `GPAC_TEST_HEADLESS`, `GPAC_TEST_CLAUDE_MODEL` and `GPAC_TEST_PROFILE_DIR` match the script options.
  - `GPAC_TEST_CLAUDE_PATH` points to `claude` when it is not on PATH.
  - `GPAC_TEST_FFMPEG_DOWNLOAD=1` runs the real ffmpeg download test.
- Tests that need ffmpeg skip themselves when it is not installed.
- All page selectors are in `GooglePhotosAutoCompressor/Selectors.cs`.
- On Linux, Chrome may need system libraries: `pwsh GooglePhotosAutoCompressor/bin/Debug/net8.0/playwright.ps1 install-deps chromium`.
- Agent notes and current status: [HANDOFF.md](HANDOFF.md).
