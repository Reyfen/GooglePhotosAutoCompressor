# GooglePhotosAutoCompressor – notes for agents

Read `HANDOFF.md` first: it holds current status, open work and known gotchas. Update it in the same commit as any change it describes.

## What this is
.NET 8 desktop app (Windows + Linux, Avalonia UI – no web/WASM version planned). It drives a real Chrome window with Playwright to:
download large videos and photos from Google Photos → compress them (ffmpeg for video, Magick.NET for JPEG) → move originals to the bin → upload the compressed copies → fix their "date taken".

- `GooglePhotosAutoCompressor/` – core library (`GooglePhotosAutoCompressor.Core.dll`): `Pipeline.cs` runs the steps (one class per step in `Modules/`), `Settings.cs`, rules in `ItemRules.cs`.
- `GooglePhotosAutoCompressor.App/` – the exe: Avalonia window (MVVM, `ViewModels/`, `Views/`), plus `--cli` and `--check`.
- `packaging/` – Linux AppImage/.deb script (`build.ps1` runs it, in WSL on Windows) and icon.

Per-item progress is stored in `~/Downloads/GooglePhotosAutoCompressor/progress.json` (with the `Originals/` and `Compressed/` work folders next to it) so runs can resume after any interruption (see "Interruptions" in HANDOFF.md).

## Rules that must hold (data safety)
- Never move an original to the bin unless its compressed copy is verified (or already uploaded).
- Never upload a compressed copy unless the original was moved to the bin (storage may be full; by design).
- Every file written (state, compressed video, downloaded ffmpeg) is written to a temp file and renamed.
- Files on disk are keyed by media key, never by the original file name alone (names collide).

## Commands
```
dotnet build
dotnet test
dotnet run --project GooglePhotosAutoCompressor.App
./build.ps1               # unit tests + Windows exe + Linux packages in dist/ (`./build.ps1 test` = only tests)
./integration_tests.ps1   # live Google Photos tests (-SignIn the first time)
```
Tests that need ffmpeg skip themselves when it is not found. Live Google Photos page tests (`Tests/Live`, `Category=Live`) need Claude Code and a signed-in profile and must stay read-only: never upload, delete or save a date in them.

## Conventions
- UI is forced to en-GB (`Const.LOCALE`) so aria-label selectors are stable. Keep all selectors in `Selectors.cs`.
- Put pure logic (parsing, rules, naming) in classes without Playwright so it can be unit tested.
- Every step takes a CancellationToken and must leave the state file consistent when stopped at any await.
- The window talks to the core only through `Pipeline`/`PipelineHost`; keep Avalonia out of the core.
- Small commits, one fix each.
- Do not run the app against a real Google account without the owner's go-ahead: it deletes items.
