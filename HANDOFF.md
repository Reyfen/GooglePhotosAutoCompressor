# Handoff

Last updated: 2026-10-27, branch `fix/robustness` (from `dev`, not merged or pushed).

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
| 16 | Live Google Photos page tests per step, checked by local Claude Code (screenshot verdict + failure diagnosis) | `Tests/Live/`, `Tests/ClaudeCode.cs` |

## Live page tests (`Tests/Live`, trait `Category=Live`)
- The owner does not want ffmpeg covered by AI tests. These cover the Google Photos pages and clicks of each step instead.
- `LivePhotosFixture` opens one Chrome with the app's profile and sets `SkipReason` with exact instructions when something is missing: Claude Code, a profile, a free profile lock, or a sign-in. Profile lookup order: `GPAC_TEST_PROFILE_DIR`, then `GooglePhotosAutoCompressor/bin/{Debug,Release}/net8.0/chrome-profile`, then LocalAppData.
- Tests reuse the app's own code where possible (`LargeFileLocator.EnumearteFiles`, `Initlizer.Launch`, `DateSetter.WaitForAnyVisibleAsync`, `Selectors`).
- `ExpectScreen` asks Claude Code a yes/no question about a screenshot. `Expect`/`Fail` wrap page actions; on failure Claude Code describes the page (`ClaudeCode.DescribeProblem`).
- Must stay read-only. The download is cancelled, dialogs are closed with Escape, and the bin dialog test is opt-in only.
- Not covered (can't be done read-only): the upload progress indicator (`UPLOAD_PROGRESS`), saving a date, confirming a delete.
- Verified 2026-10-27: build and skip messages OK. The owner's saved profile is **signed out** (lands on google.com/photos/about), so all live tests skipped. The failure path was forced once on that page: Claude Code correctly said it was the signed-out landing page. **No live test has passed yet.** It needs the owner to sign in (`GPAC_TEST_SIGN_IN=1`).

## Item lifecycle (see `ItemRules.cs`)
Listed → Downloaded (`DownloadPath`) → Compressed + `Verified` → `OriginalDeleted` → `CompressUploaded` → `DateSet` (+ `UploadedMediaKey`).
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
- CI workflow is written but has not run yet (branch not pushed).

## Not verified / needs the owner
- **Nothing has been run against live Google Photos.** Agents have no account access, and the app deletes items.
  First live run: try it on a test account, or watch it with `TOTAL_FILES_TO_PROCESS_IN_ONE_BATCH` set low.
  Things to watch:
  - The delete check (tile disappears after "Move to bin").
  - Upload detection (`Selectors.UPLOAD_PROGRESS`).
  - The scan stops at the end of the list.
  - The 12 duplicates are binned.
- **Copy placement after upload.** The date step expects new uploads at the top of the library, which matched old runs. ffmpeg always copied `creation_time`; now `+use_metadata_tags` copies more tags. If Google starts placing copies at their original date, the date step will not find them. They then stay "in progress" and block new batches once there are 10.
- **HDR (10-bit) videos.** Colours after libx264 are not checked.
- **Storage-full upload errors.** Google's error dialog is not detected; such files stay unconfirmed and are retried.

## Ideas for later
- Photo compression. The owner started `feature/photo_processing` (adds `MediaType`). It conflicts with the `FileItem`/`LargeFileLocator` changes here, so merge it by hand: photo tiles currently fail `TileParser.Parse` (no duration).
- Delete local downloads after a successful upload, to save disk space.
- Read item data from Google's `batchexecute` network responses instead of page text.
- Saved-page snapshot tests for selectors; a live smoke test on a test account; a vision-model fallback when a selector fails (could reuse `Tests/ClaudeCode.cs`).
- A GitHub release workflow that builds single-file apps for Windows and Linux.
