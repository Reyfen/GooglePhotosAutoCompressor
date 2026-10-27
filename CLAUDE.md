# GooglePhotosAutoCompressor – notes for agents

Read `HANDOFF.md` first: it holds current status, open work and known gotchas. Update it in the same commit as any change it describes.

## What this is
.NET 8 console app (Windows + Linux, desktop only – no web/WASM version planned). It drives a real Chrome window with Playwright to:
download large videos from Google Photos → compress them with ffmpeg → move originals to the bin → upload the compressed copies → fix their "date taken".

Pipeline lives in `GooglePhotosAutoCompressor/Program.cs`, one class per step in `Modules/`.
Per-item progress is stored in `~/Downloads/GooglePhotosCache.json` so runs can resume.

## Rules that must hold (data safety)
- Never move an original to the bin unless its compressed copy is verified (or already uploaded).
- Never upload a compressed copy unless the original was moved to the bin (storage may be full; by design).
- Every file written (state, compressed video, downloaded ffmpeg) is written to a temp file and renamed.
- Files on disk are keyed by media key, never by the original file name alone (names collide).

## Commands
```
dotnet build
dotnet test
dotnet run --project GooglePhotosAutoCompressor
```
Tests that need ffmpeg skip themselves when it is not found. Live Google Photos page tests (`Tests/Live`, `Category=Live`) need Claude Code and a signed-in profile and must stay read-only: never upload, delete or save a date in them.

## Conventions
- UI is forced to en-GB (`Const.LOCALE`) so aria-label selectors are stable. Keep all selectors in `Selectors.cs`.
- Put pure logic (parsing, rules, naming) in classes without Playwright so it can be unit tested.
- Small commits, one fix each.
- Do not run the app against a real Google account without the owner's go-ahead: it deletes items.
