# Timelapse Landscape Continuity Report

Date: 2026-08-25
Branch: `integration/rdance-on-construction-video-core`

## A. Root Cause

The five-scene Timelapse graph renders intermediate images in reverse order, but continuity was not represented as a first-class runtime decision. Prompt rules were also too generic for landscape profiles, so adjacent stages could be treated as independent redesigns.

## B. Current Reference Flow

The existing graph is reverse-dependent: `100 -> 80 -> 60 -> 40 -> 20 -> 0`. The worker claim query already waits for the dependency stage to be `COMPLETED`, and the existing image request accepts one reference image through the unchanged 79AI image contract.

## C. New Anchor Flow

For profiles 7A, 7B, and 7C, the immediate dependency is now explicitly selected and logged as the primary continuity anchor:

`80 <- 100`, `60 <- 80`, `40 <- 60`, `20 <- 40`, `0 <- 20`

The dependency media is passed through the existing 79AI `image` reference field. The supplied 100% image remains the destination guidance through the existing stage graph and prompt semantics.

## D. 7A Rule Changes

Installation progression now preserves installed components, flooring/deck progress, planters, benches, hardscape, permanent fixtures, camera, and layout. Prompts distinguish early setup, active installation, substantial progress, and near-finished completion, and forbid established-item loss or regression.

## E. 7B Rule Changes

Growth progression now preserves planting zones, major pot/planter locations, hardscape, growth direction, scene identity, and established greenery. Later stages explicitly cannot lose major greenery or planting layout.

## F. 7C Rule Changes

Hybrid progression now preserves introduced plants, completed flooring/deck, installed fixtures, established decor/furniture, installation state, and greenery density while advancing both tracks monotonically.

## G. Profile Storage Update

Profile rules are delivered as an idempotent manual SQL update:
`database/manual/timelapse/20260825_landscape_continuity_profiles_71_73.sql`.

It includes verification selects before and after the update and does not alter schema or execute against a database.

## H. Continuity Validation

Compiled landscape prompts are checked for adjacent-stage anchoring, scene identity, architecture/camera preservation, monotonic progress, no redesign, no regression, and profile-specific preservation rules. Missing rules fail the image submission before provider dispatch.

## I. Logging

Structured `render.render_job_events` events are emitted for:

- `TIMELAPSE_LANDSCAPE_CONTINUITY_ANCHORS`
- `TIMELAPSE_LANDSCAPE_CONTINUITY_RULES_APPLIED`
- `TIMELAPSE_LANDSCAPE_CONTINUITY_VALIDATION_PASSED`
- `TIMELAPSE_LANDSCAPE_CONTINUITY_VALIDATION_FAILED`

Event data includes profile code, progress, adjacent progress, anchor strategy, reference media ID, and validation reason/code without secrets or large payloads.

## J. UI Regression Check

Existing Timelapse UI tests remain green. Completed image thumbnail/view behavior, video preview/play behavior, draft autosave, automatic draft creation/update, readiness validation, and workflow actions remain unchanged.

## K. Files Changed

- `TodoX.Web/Services/Timelapse/TimelapseProviderRuntime.cs`
- `TodoX.Web.Tests/TimelapsePromptEditingTests.cs`
- `TodoX.Web/database/manual/timelapse/20260825_landscape_continuity_profiles_71_73.sql`
- `TodoX.Web/docs/reports/20260825_timelapse-landscape-continuity-report.md`

## L. SQL Files Changed

One new idempotent manual SQL file for profiles 71, 72, and 73. No migration or database execution was performed.

## M. Test Results

- Focused Timelapse prompt tests: **32 passed**.
- Full `TodoX.Web.Tests`: **746 passed, 0 failed, 0 skipped**.
- Changed-file formatter check: **passed**.
- Full solution formatter check: **failed on pre-existing whitespace findings in unrelated files**; no unrelated formatting was changed.
- `git diff --check`: **passed** with only line-ending normalization warnings.

## N. Build Result

`dotnet build TodoX.Dashboard.sln -c Release`: **passed, 0 warnings, 0 errors**.

## O. Publish Result

Command:

`dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`

Output:

`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

Result: **passed**.

## P. YEScale Verification

No YEScale code was changed. No YEScale fallback was introduced or reused.

## Q. 79AI Verification

No 79AI API contract, route structure, or submit payload structure was changed. Only existing prompt/reference values and structured continuity events were updated.

## R. Git

- Commit message: `fix(timelapse): enforce continuity for landscape 7a 7b 7c`
- Commit SHA: recorded in final handoff after push
- Branch: `integration/rdance-on-construction-video-core`
