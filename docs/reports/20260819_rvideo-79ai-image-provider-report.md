# RVIDEO 79AI Scene Image Pipeline Report

Date: 2026-08-19
Branch: `integration/rdance-on-construction-video-core`

## Git

- Starting SHA: `b2ad776c33ebd02f20081548bba7db227a90993d`
- Final implementation SHA: `cdca09839f3a7ccd0f151aaeef3b5ee5a9700628`
- Report commit SHA: `d6181677d408c3cb6e585d6b632476a12344d8ab`
- No migration or SQL was executed.

## IMAGE PROVIDER

- Capability: `rvideo_scene_image_generation`
- Factory: `79ai_task_image` only.
- Non-79AI resolution fails with `RVIDEO_IMAGE_PROVIDER_MUST_BE_79AI`.
- Endpoints: `POST /generateImage`, `POST /image`, exact `id_base` recovery through `POST /images`.
- Ratio normalization: `9:16 -> 9_16`, `16:9 -> 16_9`.

## MODEL POLICY

The adapter now executes one requested model/task per invocation and no longer loops through models internally.

- Primary: `google_image_gen_banana_2` / `vip` / `1k`
- Fallback 1: `imagegen_2_0` / `low_basic` / `1k`
- Fallback 2: `seedream_4_5` / `vip` / `2k`

Terminal provider failure now closes the current version and queues a new version with the next 79AI model.

## PERSISTED POLLING

- Submit once: YES
- Provider task persisted through router/outcome: YES
- Same task reused on later worker pass: YES
- Typed provider state (`Pending`, `Success`, `Failed`): YES
- Process restart safe by persisted task ID: YES
- Pending does not fail, clear task, or charge again: YES
- Transient poll retains task and requeues: YES

## REFERENCE

- JPEG/PNG/WebP data URL handling: YES
- `editImage=true` only with valid reference bytes: YES
- Missing reference fails before provider submit with `RVIDEO_REFERENCE_IMAGE_UNAVAILABLE`: YES
- Base64 is not logged.

## VERSION HISTORY

- Stale completion protection: YES
- Separate fallback image version: YES
- Mandatory end-to-end worker/router/provider integration tests: NOT COMPLETED

## BILLING, MEDIA, UI

- Poll does not recharge: YES
- Pending usage status: `pending`
- Customer image charge: deferred/zero by existing Phase 1 policy
- Provider URL copied into TodoX media on success: YES
- AUTO video transition: **DISABLED / NOT IMPLEMENTED IN THIS TASK**

## TESTS

- Focused 79AI/batch policy tests: `14 passed, 0 failed`
- Phase1B: `45 passed, 0 failed`
- Full `TodoX.Web.Tests`: `666 passed, 0 failed`
- Build and `git diff --check`: passed.
- Publish: passed.

## DATABASE

- Migration required: only if deployment lacks the existing additive seed.
- Migration path: `database/migrations/20260819_rvideo_79ai_image_provider.sql`
- SQL executed: NO
- Support SQL: `docs/support/rvideo-job-diagnostic.sql`

## PUBLISH

- Command: `dotnet publish TodoX.Web\TodoX.Web.csproj --no-restore -c Release -o artifacts\publish\todox-dashboard`
- Output: `artifacts/publish\todox-dashboard`
- Result: successful.

## FINAL IMAGE READINESS

`RVIDEO_79AI_IMAGE_NOT_READY`

Exact blockers:

1. Mandatory end-to-end fallback, all-models-failed, and full worker/router/provider integration tests are not yet present.

No video implementation was started.
