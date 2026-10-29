# GooglePhotosAutoCompressor

Frees up Google Photos storage by replacing your biggest videos and photos with smaller copies.

It opens a normal Chrome window on your own Google Photos account and, for each large item:

1. **Downloads** it from *Storage → Large photos and videos*.
2. **Compresses** it. By default videos go to half resolution and JPEG photos to at most 16 megapixels. Date taken, location and other details are kept.
3. **Checks** the copy. If anything looks wrong, or it would not save at least 10%, the original is left alone.
4. **Moves the original to the bin**, after asking you. Google keeps it there for 60 days, so you can still restore it.
5. **Uploads** the smaller copy.
6. **Fixes the date** of video copies so they appear in the right place in your timeline.

**Stop it at any time.** Press Stop, close the app, or lose the internet: the next run carries on where it stopped, and nothing is moved to the bin before its copy is checked. Everything it downloads, and its progress, is kept in one folder: `Downloads/GooglePhotosAutoCompressor`.

**Before you start:** compression loses some quality, and once the bin is emptied the originals are gone for good. The app clicks through the Google Photos website, which may be against Google's terms, and a change to that site can break it. Use it on your own account at your own risk.

## Download

Get the latest version from the [Releases page](https://github.com/Reyfen/GooglePhotosAutoCompressor/releases).

| System | File | How to start |
|--------|------|--------------|
| Windows 10/11 | `GooglePhotosAutoCompressor-<version>-win-x64.exe` | Double-click. Nothing to install. |
| Any Linux | `GooglePhotosAutoCompressor-<version>-x86_64.AppImage.tar.gz` | Unpack, then double-click the `.AppImage`. |
| Ubuntu, Debian, Mint | `google-photos-auto-compressor_<version>_amd64.deb` | Double-click to install, or `sudo apt install ./google-photos-auto-compressor_*.deb`. |

You also need **Google Chrome**. Without it the app installs Chromium, but Google sign-in may refuse it.
Videos need **ffmpeg**: the Windows exe includes it, the `.deb` installs it, and the AppImage offers to download it.

## Using the app

- **Run:** press **Start**. The first time, sign in to Google in the Chrome window that opens; you stay signed in. Six steps show where it is; the cards show the space saved and how many items are done, in progress, waiting or left alone. The activity log shows every action.
- **Items:** every item found, with its size, the size of its copy and its status. Filter and search; give failed items new attempts; delete local files of finished items.
- **Settings** (saved straight away):
  - what to compress (videos, photos, size thresholds);
  - quality (video size and quality, photo size and quality, smallest saving worth it);
  - how much per run (items per batch, keep going until done, parallel downloads, free disk space to keep, attempts per item);
  - safety (ask before moving to the bin, delete local files when done, work folder).
- **Help:** how it works, **Check setup** (tests ffmpeg, photos, the browser, folders and internet without opening Google Photos), and shortcuts to the Google Photos bin and the log.

The app keeps the PC awake during a run and asks before closing while a run is going. Only one copy runs at a time.

Without a window (terminal or server): `GooglePhotosAutoCompressor --cli` runs one session with the saved settings (`--yes` skips the bin question), and `GooglePhotosAutoCompressor --check` runs the setup check.

## Build and test

| Script | What it does | Needs |
|--------|--------------|-------|
| `./build.ps1` | Builds the app and runs the unit and UI tests. | .NET 8 |
| `./integration_tests.ps1` | Tries every step on the real Google Photos website and lets Claude Code check the screenshots. | Chrome, [Claude Code](https://claude.com/claude-code), a signed-in Google account |
| `./publish.ps1` | Makes the portable Windows exe in `dist/` and runs its setup check. | .NET 8, Git LFS (for the bundled ffmpeg) |
| `packaging/linux/build-packages.sh` | Makes the AppImage `.tar.gz` and the `.deb` in `dist/` (run on Linux). | .NET 8, `dpkg-deb` |

On Linux, run the `.ps1` scripts with `pwsh` (for example `pwsh ./build.ps1`). Run the app from source with `dotnet run --project GooglePhotosAutoCompressor.App`.

`build.ps1` options: `-Configuration Release`, `-SkipTests`. `publish.ps1` options: `-Version 1.2.0`, `-Runtime win-arm64`.

The **Packages** GitHub workflow builds all three files on every push and tests them on clean machines: a fresh Windows VM, a fresh Ubuntu VM (AppImage started as a double-click would, with Chrome), and clean Ubuntu 22.04/24.04, Debian 12 and Fedora 41 containers. Each test runs the setup check, opens the window on a virtual screen and keeps a screenshot. Pushing a tag such as `v1.2.0` publishes a release with the files.

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

- Projects: `GooglePhotosAutoCompressor` (core library: pipeline, steps, rules), `GooglePhotosAutoCompressor.App` (Avalonia window, the exe), `GooglePhotosAutoCompressor.Tests` (unit and live tests), `GooglePhotosAutoCompressor.UiTests` (window tests drawn without a screen; screenshots go to `gpac-ui-screens` in your temp folder).
- Plain `dotnet test` runs everything, including the integration tests. Use `--filter "Category!=Live"` to leave them out.
- Test settings (environment variables):
  - `GPAC_TEST_LIVE=0` turns the integration tests off.
  - `GPAC_TEST_SIGN_IN`, `GPAC_TEST_LIVE_BIN_DIALOG`, `GPAC_TEST_HEADLESS`, `GPAC_TEST_CLAUDE_MODEL` and `GPAC_TEST_PROFILE_DIR` match the script options.
  - `GPAC_TEST_CLAUDE_PATH` points to `claude` when it is not on PATH.
  - `GPAC_TEST_FFMPEG_DOWNLOAD=1` runs the real ffmpeg download test.
- Tests that need ffmpeg skip themselves when it is not installed.
- All page selectors are in `GooglePhotosAutoCompressor/Selectors.cs`.
- Settings and logs live in `%LOCALAPPDATA%\GooglePhotosAutoCompressor` (Windows) or `~/.local/share/GooglePhotosAutoCompressor` (Linux).
- Agent notes and current status: [HANDOFF.md](HANDOFF.md).
