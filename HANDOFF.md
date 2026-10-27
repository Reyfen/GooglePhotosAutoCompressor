# Handoff

Last updated: 2026-10-27, branch `fix/robustness` (from `dev`).

## Status
Robustness pass in progress. Each item below is its own commit.

| # | Fix | Status |
|---|-----|--------|
| 1 | Batch constants in `Const`, unreadable size skipped | done |
| 2 | Test project + `TileParser` split out | done |
| 3 | State file: lock + atomic write | todo |
| 4 | Download/compress paths unique per media key | todo |
| 5 | Delete step: no 21-miss cutoff, only verified items, recovers 12 duplicates | todo |
| 6 | Upload step: only after original is binned | todo |
| 7 | Compressor: temp file, verify output, safer ffmpeg args | todo |
| 8 | Upload completion detection | todo |
| 9 | Date setter: consecutive-miss limit, record uploaded media key | todo |
| 10 | Large-files scan: detect end of list | todo |
| 11 | Downloader: one failure does not stop the run, zip guard | todo |
| 12 | Skip our own compressed uploads (no double compression) | todo |
| 13 | Linux support | todo |
| 14 | Prompted ffmpeg download | todo |
| 15 | CI + README | todo |

## Known facts from the owner's real state file (2026-10-27)
- 82 items; 38 fully done; 32 skipped as low bitrate (< 0.8 MB/s).
- 12 items were uploaded but the original was never binned → duplicates in Google Photos.
  Cause: the delete step counted low-bitrate items as "unknown" and stopped after 21 of them;
  the upload step did not check that the original was binned. Fix #5 bins those 12 on the next run.

## Not verified / needs the owner
- Nothing in this branch has been run against live Google Photos (no account access for agents).
  Selectors and the upload-finished detection need a live check, ideally on a test account.
- HDR (10-bit) videos: output keeps whatever pixel format libx264 picks; colours not checked.

## Ideas for later
- Photo compression (owner started on `feature/photo_processing`: adds `MediaType`; will conflict with `FileItem` changes here – merge by hand).
- Read item data from Google's `batchexecute` network responses instead of page text.
- Saved-page snapshot tests for selectors; live smoke test on a test account; vision-model fallback when a selector fails.
- GitHub release workflow producing single-file builds for Windows and Linux.
