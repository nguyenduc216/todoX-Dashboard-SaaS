# Timelapse Video Forward Progression Report

Date: 2026-08-25
Branch: `integration/rdance-on-construction-video-core`

## A. Root Cause

The persisted image pair mapping was already forward in the current worker query, but there was no centralized direction guard and the compiled video prompt was too weak to prevent reverse-looking motion. The fix addresses both:

- validated forward pair before 79AI submit;
- explicit forward-only and profile-specific motion rules;
- event evidence containing the actual stage/media pair;
- current stage media is loaded again for every video claim, including rerenders.

No reverse image-pair swap was found in the current code path.

## B. Mapping Before Fix

The current SQL joined `start_img.progress_percent = c.start_progress_percent` and `end_img.progress_percent = c.end_progress_percent`, then serialized descriptors in `[startDescriptor, endDescriptor]` order:

| Clip | Start | End |
|---|---:|---:|
| 0 -> 20 | image 0 | image 20 |
| 20 -> 40 | image 20 | image 40 |
| 40 -> 60 | image 40 | image 60 |
| 60 -> 80 | image 60 | image 80 |
| 80 -> 100 | image 80 | image 100 |

The same forward edge construction applies to 3-, 4-, and 6-scene graphs using their configured progress mappings.

## C. Mapping After Fix

The mapping remains the same and is now enforced before submit. Reverse, flat, stage-mismatched, missing-stage, missing-media, and same-media pairs fail deterministically.

## D. 79AI Request Contract

The existing Timelapse contract remains unchanged:

- operation: `Video`;
- submit: existing configured Timelapse 79AI video path;
- poll: existing configured Timelapse 79AI video path;
- request transport: existing `Ai79TaskClient.SubmitAsync` form-urlencoded flow;
- Timelapse image pair: existing `options["images"]` JSON array;
- array order: first descriptor is the earlier/start image, second descriptor is the later/end image;
- task id extraction and poll semantics are unchanged.

## E. Prompt Changes

The mandatory compiled prompt now includes:

- forward chronological progression;
- begin from the earlier-progress reference state;
- end at the later-progress reference state;
- the scene becomes progressively more complete;
- never reverse construction or landscaping progress;
- never dismantle completed flooring, deck, planters, fixtures, or permanent elements;
- do not remove elements belonging to the later stage;
- preserve the same architecture, camera, lens, perspective, framing, and environment;
- monotonic intermediate motion from earlier to later completion.

## F. Profile Rules

- `landscape_balcony_install_v1`: install/add components, complete flooring/deck, place planters/furniture, clean toward the later state, and never dismantle installed work.
- `landscape_garden_growth_v1`: increase plant density, foliage, and maturity in the same zones; never shrink or remove established greenery.
- `landscape_balcony_hybrid_v1`: advance installation and greenery together; never remove flooring, make plants disappear, or regress the hybrid layout.

## G. Logging

Added or confirmed events:

- `TIMELAPSE_VIDEO_DIRECTION_VALIDATION_BEGIN`
- `TIMELAPSE_VIDEO_DIRECTION_VALIDATION_PASSED`
- `TIMELAPSE_VIDEO_DIRECTION_VALIDATION_FAILED`
- `TIMELAPSE_VIDEO_PROMPT_COMPILED`
- `TIMELAPSE_VIDEO_SUBMIT_BEGIN`
- `TIMELAPSE_VIDEO_SUBMIT_RESPONSE`
- `TIMELAPSE_VIDEO_SUBMITTED`
- `TIMELAPSE_VIDEO_POLL_RESPONSE`
- `TIMELAPSE_VIDEO_COMPLETED`
- `TIMELAPSE_VIDEO_FAILED`

Submit evidence includes job/clip, progress pair, stage IDs, media IDs, public URLs, provider/model, task ID where available, and `direction: forward`. Secrets and binary payloads are excluded.

## H. Rerender Behavior

`ClaimVideoAsync` resolves the current completed start/end stage rows and their active media/version response data each time a clip is claimed. Therefore an upstream image rerender supplies the newest current media to the dependent video clip.

## I. Files Changed

- `TodoX.Web/Models/Timelapse/TimelapseModels.cs`
- `TodoX.Web/Services/Timelapse/TimelapseProviderRuntime.cs`
- `TodoX.Web/Services/Timelapse/TimelapseWorkerRepository.cs`
- `TodoX.Web.Tests/TimelapseVideoDirectionTests.cs`
- `docs/reports/20260825_timelapse-video-forward-progression-report.md`

## J. Test Results

- Focused Timelapse direction tests: passed, `18/18`.
- Full `TodoX.Web.Tests`: passed, `764/764`.
- `git diff --check`: passed.
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore`: failed on pre-existing whitespace findings across unrelated files; no unrelated formatting changes were made.

## K. Build Result

`dotnet build TodoX.Dashboard.sln -c Release --no-restore`: passed with 0 errors. Existing Razor/nullability warnings remain.

## L. Publish Result

`dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`: passed.

Output:

`D:\todoX\Dashboard-web\TodoXPortal\TodoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

## M. YEScale Verification

- No YEScale code was modified.
- No YEScale fallback was introduced.

## N. 79AI Verification

- No 79AI API contract change.
- No route change.
- No provider task ID semantic change.

## O. Git

Commit message: `fix(timelapse): enforce forward-only video progression`
