# Timelapse Image Fallback JSON Fix Report

## A. ROOT CAUSE

Yes. `Utf8JsonWriter` could still hold buffered JSON bytes when `buffer.ToArray()` was read. The fallback payload could therefore be incomplete before `CAST(@requestJson AS jsonb)` / `CAST(@responseJson AS jsonb)`, causing PostgreSQL `22P02: invalid input syntax for type json`.

## B. EXACT CODE FIX

Before:

`write -> buffer.ToArray() -> writer dispose`

After:

`write -> writer.Flush() / dispose -> buffer.ToArray()`

Both successful serialization paths in `AppendImageModelAttempt` now flush the writer before reading the memory buffer. JSON structure and `image_model_attempts` behavior remain unchanged.

## C. FILES CHANGED

- `TodoX.Web/Services/Timelapse/TimelapseProviderRuntime.cs`: flush `Utf8JsonWriter` before reading `MemoryStream` in every successful return path.
- `TodoX.Web.Tests/TimelapsePhase2CTests.cs`: focused regression test that invokes `AppendImageModelAttempt` and parses the output for empty input, `worker_claim`, existing attempts, Unicode, quotes, newline, and backslash.
- `docs/reports/20260825_timelapse_image_fallback_json_fix_report.md`: this result report.

## D. TESTS

- Focused JSON regression: passed, `1/1`.
- Full suite: passed, `722/722`.
- `git diff --check`: passed.

## E. BUILD

Passed: `dotnet build TodoX.Dashboard.sln -c Release --no-restore`.

## F. PUBLISH

Passed: `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`.

Output: `artifacts/publish/todox-dashboard`.

## G. YESCALE VERIFICATION

- No YEScale code was touched.
- No YEScale execution path was introduced.
- Timelapse image remains 79AI-only.

## H. OUT-OF-SCOPE

None discovered or fixed. Provider, fallback order, API contract, repository, claim architecture, UI, billing, video, finalizer, RVideo, RDance, schema, and migrations were left unchanged.

## I. GIT

- Commit SHA: `5f288d1`
- Commit message: `fix(timelapse): flush image fallback json before persistence`
- Pushed branch: `integration/rdance-on-construction-video-core`
