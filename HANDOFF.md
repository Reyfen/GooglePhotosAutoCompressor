# Handoff

Last updated: 2026-10-29, branch `fix/robustness` (`dev` was fast-forwarded to 1529751 on 2026-10-28).

## Status
Robustness pass finished: one commit per fix (`git log dev..fix/robustness`).

| # | Fix | Where |
|---|-----|-------|
| 1 | Batch constants in `Const`, unreadable size skipped | `Const.cs`, `TileParser.cs` |
| 2 | Test project + `TileParser` split out | `GooglePhotosAutoCompressor.Tests/` |
| 3 | State file: lock + temp-file write + `.bak` fallback | `FileItemsData.cs` |
| 4 | Download/compress paths unique per media key | `FilePaths.cs` |
| 5 | Delete step: no 21-miss cutoff, only verified/uploaded items | `Modules/Deletor.cs`, `ItemRules.cs` |
| 6 | Upload only after original is binned | `ItemRules.CanUpload` |
| 7 | Compressor: `.part` file, duration + size check, even scaling, metadata, AAC fallback | `Modules/Compressor.cs`, `Ffmpeg.cs` |
| 8 | Upload: unconfirmed uploads not marked; 30 min limit; selectors in one file | `Modules/Uploader.cs`, `Selectors.cs` |
| 9 | Date setter: consecutive-miss limit, confirms upload, records uploaded media key | `Modules/DateSetter.cs` |
| 10 | Large-files scan: end-of-list detection; delete step treats unlisted originals as binned | `Modules/LargeFileLocator.cs` |
| 11 | Downloader: per-item failures, zip guard | `Modules/Downloader.cs` |
| 12 | Never re-compress own uploads (media key or `_compressed` name) | `Modules/Downloader.cs` |
| 13 | Linux: Chromium fallback, profile location, sign-in without timeout | `Modules/Initlizer.cs` |
| 14 | Prompted ffmpeg download with SHA-256 check | `FfmpegInstaller.cs` |
| 15 | CI (Windows + Linux), README, LFS pointer guard | `.github/workflows/ci.yml` |
| 27 | `publish.ps1` merged into `build.ps1` (targets `test`/`win`/`linux`): one line per step, full output in `dist/logs/`, list of files at the end. Linux files on Windows: published here, packed in WSL (`Ubuntu-24.04`) on the Linux filesystem (a /mnt/c folder loses the AppImage's exec bit), as in Timbratune | `build.ps1`, `packaging/linux/build-packages.sh` |
| 26 | Versions are named `v1.0.0`: file names, release tags and the window header. The number inside the exe and the .deb `Version:` field stays `1.0.0` (both must start with a digit). Scripts accept `1.0.0` or `v1.0.0` | `publish.ps1`, `packaging/linux/build-packages.sh`, `.github/workflows/packages.yml`, `MainViewModel.VersionText` |
| 25 | First-start page: the main choices (after each batch: stop or carry on; batch size; ask before bin; videos/photos and video size; delete local files; work folder), shown until answered (`Settings.SetupDone`, so older settings files see it once too); says the Settings tab changes them later; Settings has "Show the first-start choices again" | `MainWindow.axaml` (`SetupPage`), `SettingsViewModel` |
| 24 | All app files in `Downloads/GooglePhotosAutoCompressor` (`progress.json`, `Originals/`, `Compressed/`); files of older versions are moved there once on start, progress kept (`AppFolderMigration`: new state first, then folders, then old state removed; tested for a cut-off move) | `AppFolderMigration.cs`, `Const.APP_DOWNLOADS_PATH`, `FilePaths.cs` |
| 23 | Date **and time** (and time zone) of each original are read from Google Photos before the bin and given to the copy (see "Date and time taken") | `TakenTime.cs`, `Modules/TakenTimeReader.cs`, `Modules/DateDialog.cs` |
| 22 | Packages: portable Windows exe (`publish.ps1`), AppImage `.tar.gz` + `.deb` (`packaging/linux/build-packages.sh`), `--check` setup check, `Packages` workflow testing them on clean VMs/containers | `publish.ps1`, `packaging/`, `.github/workflows/packages.yml`, `SetupCheck.cs` |
| 21 | Avalonia desktop app (Run / Items / Settings / Help), `--cli`, UI tests drawn headless | `GooglePhotosAutoCompressor.App/`, `GooglePhotosAutoCompressor.UiTests/` |
| 20 | Settings (`settings.json` in LocalAppData) replace the batch/threshold/quality constants; `Pipeline` runs the steps with Stop, progress, retry and internet waits; core is now a library (`GooglePhotosAutoCompressor.Core.dll`) | `Settings.cs`, `Pipeline.cs`, `AppSupport.cs` |
| 19 | Resume after crash / power cut / internet loss (see "Interruptions" below) | `DurableFile.cs`, `Network.cs`, all modules |
| 18 | Root scripts: `build.ps1` (build + unit tests, Live excluded), `integration_tests.ps1` (live tests; options set env vars for the run only and restore them). README rewritten for non-developers | `build.ps1`, `integration_tests.ps1`, `README.md` |
| 17 | Photo compression (merged `feature/photo_processing`): JPEG via Magick.NET, EXIF kept, no date step | `PhotoCompressor.cs`, `TileParser.cs`, `ItemRules.cs` |
| 16 | Live Google Photos page tests per step, checked by local Claude Code (screenshot verdict + failure diagnosis) | `Tests/Live/`, `Tests/ClaudeCode.cs` |

## Interruptions (app closed, killed, power cut, internet lost)
Every step only acts on what the state file says is ready, and the file is saved after each change, so a re-run carries on. What covers each case:

| Case | What happens | Code |
|------|--------------|------|
| Power cut while saving state | Temp file is flushed to disk before the rename; an empty or `null` file is treated as unreadable and the `.bak` is used | `DurableFile`, `FileItemsData.Load` |
| Killed during download or encode | `.part` files are never mistaken for finished ones and are deleted at the start of the next run | `FilePaths.IsPartialFile/DeletePartialFiles` |
| Killed after a copy was renamed, before the save | The finished copy is found by its expected name and checked again | `Compressor.AdoptFinishedCopy` |
| Internet drops during a step | The step fails, the pipeline waits for the connection and runs the step again (all steps resume) | `Pipeline.Step`, `Network` |
| Internet drops during a download | Not counted as an item failure (otherwise items get abandoned after 3 drops) | `Downloader.CountFailure` |
| Internet drops during an upload | Upload not marked done when any check failed since it started; re-uploaded next run (Google ignores identical re-uploads) | `Uploader`, `Network.WasOfflineSince` |
| Upload looked done but never arrived | Date step does not find the copy 3 runs in a row, so it is uploaded again | `DateSetter.CountMiss` |
| Killed between "Move to bin" and saving | Next run: original is not in the list any more, marked as binned (existing rule) | `Deletor` |
| Stop pressed | Checked between items; ffmpeg is killed and its `.part` deleted; downloads cancelled; nothing counts as a failure | all modules take a `CancellationToken` |
| Chrome window closed | Recognised and reported; run stops | `Pipeline.IsBrowserClosed` |
| Disk full | Downloads stop before the disk drops under `MinFreeDiskGb` | `DiskSpace` |
| Two copies of the app | Second one cannot get the lock file | `AppLock` |

Not covered: a photo copy whose upload was wrongly reported as done (photos have no date step to notice it). Rare: needs the progress bar to vanish while the connection checks every 5 s all pass.

## Date and time taken
Before 2026-10-29 the date step typed only year/month/day. A video copy kept its time only through the file's `creation_time`, so a video without one, or one whose time was edited in Google Photos, got the wrong time.
- New step "Reading date and time of the originals" (`TakenTimeReader`, stage `ReadingDates`) runs before the bin. For each item ready for the bin it opens `photos.google.com/photo/<media key>`, opens the "Edit date and time" dialog, reads Year/Month/Day/Hour/Minutes and the time zone, and closes it with Escape (never saves). Stored in `FileItem.TakenAt` (wall-clock, minute) and `TakenOffsetMs` (Google's own unit: the time zone list's `data-value`).
- `ItemRules.IsReadyForBin` = `CanDeleteOriginal` + time known. The read is retried for 3 runs (`TakenTime.READ_TRIES`, `TakenReadMisses`); after that the original may go anyway and the copy keeps the file's own time. A read whose date differs from the list date counts as a miss (wrong page).
- Videos: the date step types the date, hour, minutes, and picks the time zone **first** (picking a zone shifts the shown time to keep the same moment — found in a live check). `DateDialog.Fill` reads the fields back and refuses to save if they differ.
- Photos: still no date step (Google places them by EXIF). If Google's time differs from the copy's EXIF (edited in Google Photos), `PhotoCompressor.ApplyTakenTime` makes the copy again from the download with that `DateTimeOriginal` + `OffsetTimeOriginal`.
- Dialog facts (live, 2026-10-29): inputs `aria-label` Year, Month, Day, Hour, Minutes (24 h); time zone `div[role=combobox]` shows "GMT+01:00 British Summer Time"; options `[role=option][data-value=<ms>]`; the first Escape closes only an open time zone list. The info panel's "Date taken:" label has the date only.
- Fixed 2026-10-29 after the owner's first live run: the reader also picked the 12 old items whose copies were uploaded and dated by an older version but whose originals were never binned. Those originals never load in the viewer (spinner, no info panel even after 45 s; not on the large files page either), so each read failed with "panel not found" and the run spent its time there. Now `NeedsTakenTime` skips items with `DateSet` (the time would not be used), and a viewer that does not show its info within ~15 s fails with "the item did not load in the viewer". The 10 new items of that run read fine (e.g. 8 Jun 2026, 22:59 GMT+02:00).
- Verified live, read-only: reading an original by media key; typing another time and zone into the dialog and cancelling (values unchanged afterwards). Not verified: an actual save (the live tests must not save).

## Desktop app (`GooglePhotosAutoCompressor.App`)
- Assembly name `GooglePhotosAutoCompressor` (the exe); the core is `GooglePhotosAutoCompressor.Core.dll` (library). `InternalsVisibleTo` lets the app and tests use the internal core types.
- Avalonia 11.3.22 (not 12: the 11 API is known and stable), CommunityToolkit.Mvvm, compiled bindings. `ViewModels/MainViewModel` talks to the core only through `Pipeline` + `PipelineHost` callbacks (marshalled to the UI thread).
- Log: every `Console.WriteLine` goes through `TeeTextWriter` to the window and to `<config>/logs/*.log` (last 30 kept).
- UX features: safe Stop, six-step progress, per-video ffmpeg progress, space saved + counts, items list with filter/search, retry failed, free up disk space, confirm-before-bin dialog (with "don't ask again"), settings saved instantly and locked during a run, keep PC awake (Windows `SetThreadExecutionState`, Linux `systemd-inhibit`), ask before closing mid-run, single instance (`AppLock`), Check setup, light/dark theme from the OS.
- `--cli [--yes]` runs one session in a terminal; `--check [--no-browser]` runs the setup check (exit code 0 = OK; the internet result does not count).
- Run page: no overall progress bar (it repeated the steps). Small outlined bars join the six step circles in one grid row (aligned with the circles' middle, checked by a UI test): green once the next step is done, blue and filled with the running step's progress, grey ahead. Progress comes from `StepProgress` (core), which each step reports as items finish: downloads finished of the free batch places, compress files plus ffmpeg's share of the current one, times read, tiles selected for the bin, uploads, copies dated. Sign-in has no measure. `Pipeline.Step` resets it to 0. Long activity lines wrap (no sideways scrolling).
- Headless screenshots: call `AvaloniaHeadlessPlatform.ForceRenderTimerTick()` before `CaptureRenderedFrame`, or a looping animation can make it return an old frame.
- Radio buttons must not use `GroupName`: a named group spans windows, and in the UI tests (one window per test, each with its own settings) two windows' radios flipped each other forever (hang found with `--blame-hang` + `dotnet-dump`). Radios in one panel group by themselves.
- UI tests (`GooglePhotosAutoCompressor.UiTests`) use Avalonia.Headless with Skia and save screenshots to `%TEMP%/gpac-ui-screens`.
- The app uses its own browser profile in `<config>/chrome-profile`, so the owner signs in once more in the app. The live tests still use the old `GooglePhotosAutoCompressor/bin/Debug/net8.0/chrome-profile`.

## Packages
- Names: `GooglePhotosAutoCompressor-v1.0.0-win-x64.exe`, `GooglePhotosAutoCompressor-v1.0.0-x86_64.AppImage.tar.gz`, `google-photos-auto-compressor_v1.0.0_amd64.deb`. A `v1.2.0` tag makes a release; other pushes build `v1.0.0-ci.<run>`.
- Windows: one self-contained single-file exe (~145 MB, ffmpeg and the Playwright driver inside, unpacked to `%TEMP%/.net` on first start). Needs Git LFS locally for the bundled ffmpeg; CI downloads ffmpeg from BtbN instead (saves LFS bandwidth).
- Linux: self-contained folder in an AppImage (tar.gz keeps the exec bit) and in a .deb (`/opt/google-photos-auto-compressor`, `/usr/bin/google-photos-auto-compressor`, desktop entry and icon). `InvariantGlobalization` = no libicu dependency. The .deb `Recommends: ffmpeg`; the AppImage offers to download it.
- `<config>` = `Settings.CONFIG_DIR`. On Linux .NET returns "" for LocalApplicationData when `~/.local/share` does not exist; `Settings.LocalAppData()` creates it (found by the package tests: the first CI run wrote to the current folder).
- Verified 2026-10-27 by the Packages workflow (run 40582378937): Windows VM (setup check incl. Chrome, window opened, screenshot), Ubuntu 24.04 VM (AppImage mounted with FUSE like a double-click, and .deb, both with Chrome), containers Ubuntu 22.04/24.04 .deb, Debian 12 .deb + AppImage, Fedora 41 AppImage. All passed; screenshots show the window drawn. Not tested: a real double-click in a desktop file manager, arm64, Linux Mint/other desktops.
- Linux packages build locally through WSL (`Ubuntu-24.04`, default user root: has `dpkg-deb` and `curl`, no .NET needed). C: had about 100 GB free on 2026-10-29. Verified: `./build.ps1` made all three files in about 1.5 minutes.

## Live page tests (`Tests/Live`, trait `Category=Live`)
- The owner does not want ffmpeg covered by AI tests. These cover the Google Photos pages and clicks of each step instead.
- `LivePhotosFixture` opens one Chrome with the app's profile and sets `SkipReason` with exact instructions when something is missing: Claude Code, a profile, a free profile lock, or a sign-in. Profile lookup order: `GPAC_TEST_PROFILE_DIR`, then `GooglePhotosAutoCompressor/bin/{Debug,Release}/net8.0/chrome-profile`, then LocalAppData.
- Tests reuse the app's own code where possible (`LargeFileLocator.EnumearteFiles`, `Initlizer.Launch`, `DateSetter.WaitForAnyVisibleAsync`, `Selectors`).
- `ExpectScreen` asks Claude Code a yes/no question about a screenshot. `Expect`/`Fail` wrap page actions; on failure Claude Code describes the page (`ClaudeCode.DescribeProblem`).
- Must stay read-only. The download is cancelled, dialogs are closed with Escape, and the bin dialog test is opt-in only.
- Not covered (can't be done read-only): the upload progress indicator (`UPLOAD_PROGRESS`), saving a date (filling it and cancelling is covered), confirming a delete.
- Verified 2026-10-27: the owner signed in with `GPAC_TEST_SIGN_IN=1` (wait is now 15 min) and the profile stays signed in. Result: 7 passed, 1 skipped (bin dialog, opt-in). The large files page is a **single-column list**, not a grid; the AI question for it was too strict and was fixed. The scan read 30 tiles correctly (size, duration, date).

## Photos (`PhotoCompressor.cs`)
- `feature/photo_processing` was merged into this branch (5023dc4). Taken over:
  - a tile without a duration is a photo (`MediaType.Image`)
  - a tile whose size can't be read is skipped
  - the compress step prints a summary at the end
- Not taken over:
  - the `FileItem` constructor, whose misspelt `duraion` parameter would break loading old state files
  - the debug `Console.ReadLine`
  - the batch size of 50 (it stays at 10)
- The downloaded file's type decides how an item is compressed, not the tile: a video tile whose duration hasn't loaded yet looks like a photo. `.jpg`/`.jpeg` go to `PhotoCompressor`; other photo types are skipped.
- JPEG: at most 16 MP (`Settings.MaxPhotoMegapixels`), quality 85 (`Settings.PhotoJpegQuality`). EXIF, ICC and XMP are kept. A photo without an EXIF date taken is skipped, because Google would place the copy at the upload date.
- Check: the copy opens, and proportions (within 1%), orientation and date taken are unchanged. Size must be ≤ 90%, as for videos.
- No date step for photos: the upload step sets `DateSet` for JPEG copies (a time edited in Google Photos is written into the copy's EXIF before the bin, see "Date and time taken"). Google places them by EXIF date, not at the top of the library, so the date step would not find them anyway. Photo copies don't record `UploadedMediaKey`. The `_compressed` name check in the download step still stops them being compressed twice.
- ffprobe was considered (owner's question): it can read photo tags, but ffmpeg can't write EXIF into JPEGs, so Magick.NET is used. ffprobe for reading video duration would be a small, separate improvement.
- Verified: unit tests on Windows (111 passed) and on Linux in WSL Ubuntu 24.04 (72 photo/rule/parser/state tests passed). Not tested on real phone photos or live.

## Item lifecycle (see `ItemRules.cs`)
Listed → Downloaded (`DownloadPath`) → Compressed + `Verified` → `TakenAt` read → `OriginalDeleted` → `CompressUploaded` → `DateSet` (+ `UploadedMediaKey`).
Side exits: `CompressionSkipped` (not worth it / unsupported / own copy) and `FailCount >= 3`. Both leave the original alone.

## Facts from the owner's state file (2026-10-27)
- 82 items: 38 fully done, 32 skipped as low bitrate (< 0.8 MB/s).
- 12 items were uploaded but their original was never binned, so they exist twice in Google Photos.
  Cause: fixes #5/#6. The next run's delete step moves those 12 originals to the bin.

## Verified (2026-10-27)
- Windows: 72 passed, 1 skipped (the real download test, which needs `GPAC_TEST_FFMPEG_DOWNLOAD=1`).
- Linux (WSL Ubuntu 24.04, .NET 8, at commit 96ec0fb):
  - No ffmpeg on PATH: the real download test downloaded, checked and ran ffmpeg. The 8 ffmpeg-dependent tests skipped.
  - BtbN Linux ffmpeg on PATH: 71 passed, 1 skipped (the download test, switched off for that pass).
- The browser part was not run on Linux (needs a desktop session and a Google sign-in).
- CI runs on GitHub (Windows + Ubuntu); the Windows job failed until 2026-10-27 because a test expected the LFS-pointer ffmpeg.exe to be used (fixed).

## Not verified / needs the owner
- **The full app has not been run against live Google Photos** (only the read-only page tests). The app deletes items.
  First live run: try it on a test account, or watch it with "Items per batch" set low (Settings page). Keep "Show what will be moved and ask first" on.
  Things to watch:
  - The delete check (tile disappears after "Move to bin").
  - Upload detection (`Selectors.UPLOAD_PROGRESS`).
  - The scan stops at the end of the list.
  - The 12 duplicates are binned.
- **Copy placement after upload: happened, fixed 2026-10-29.** In the owner's first live run (10 videos) the copies kept their creation time, so Google filed them at their original dates and the date step (which looked at the top of the library) found none. The date step now starts from "Recently added" (`Selectors.RECENTLY_ADDED_URL`, by upload time), where all 10 were the newest items. Checked read-only: every copy already showed exactly its original's date, time and zone, so the step now reads the dialog first and only saves when something differs (`TakenTime.AlreadyShown`). Not yet seen: the next app run marking those 10 as done.
- **HDR (10-bit) videos.** Colours after libx264 are not checked.
- **Storage-full upload errors.** Google's error dialog is not detected; such files stay unconfirmed and are retried.

## Ideas for later
- Read item data from Google's `batchexecute` network responses instead of page text.
- Saved-page snapshot tests for selectors; a live smoke test on a test account; a vision-model fallback when a selector fails (could reuse `Tests/ClaudeCode.cs`).
