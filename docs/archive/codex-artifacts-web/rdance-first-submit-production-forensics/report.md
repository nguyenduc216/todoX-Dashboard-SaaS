# RDance First Submit Production Forensics

Audit date: 2026-09-18

Repository HEAD observed: `42f606538a1e6b807adb965f08efafeedc90d07e`

Mode: read-only production database audit. No production code, tests, migrations, provider tasks, retries, deployments, commits, or pushes were performed.

Evidence file: `artifacts/codex/rdance-first-submit-production-forensics/evidence.json`

## 1. Executive Summary

Production evidence does not support the assumption that the required Dance Sell job succeeded on retry. The required job `5ec35e57-4958-4991-b833-2e8c05e9debb` has four recorded motion attempts and all four failed.

The first render `4db26687-1883-4a18-a54a-23ceaf0cc502` failed before provider submit because the reference image upload could not open the multipart `file` field. The known failed render `5013d57f-aa63-408f-8a0f-57cbcff0c605` is a retry of `4db26687...`; it uploaded and list-verified the reference image and motion video, then 79AI returned provider error `2100` with message `Tải media lên thất bại, vui lòng kiểm tra file và thử lại.` No provider task ID was created. Two later retries, `976685e0-66ea-4889-b94d-93ab79c02fb8` and `ad2ed483-9d92-477c-bd4a-ce382ccf2b6a`, failed with the same submit-stage provider error.

Root cause classification: `F. JOB / INPUT SPECIFIC`.

The evidence points to a specific motion asset/provider media combination failure, not a general 79AI outage or code regression. Successful comparison jobs used the same provider/model, same submit endpoint, same form-url-encoded contract, and in one success case the same reference provider image ID, but different motion provider video IDs and smaller motion video files.

## 2. Production DB Access

Accessed `TodoXSaaS` with SELECT-only queries.

Relevant schemas/tables read:

| Schema | Tables |
|---|---|
| `dance_sell` | `dance_sell_jobs`, `dance_sell_provider_operations`, `dance_sell_reference_versions` |
| `render` | `render_jobs`, `render_job_events`, `render_job_inputs` |
| `public` | `todox_ai_operation_assets` |
| `media` | `media_files` |

`TodoXAutomation` was also checked earlier. It contains legacy `public.todox_*` tables but returned 0 rows for the required job/render/success IDs. The current production evidence is therefore from `TodoXSaaS`.

Credential/provider-key tables were not queried for row data. JSON and URLs in `evidence.json` were sanitized for token/secret/key-like fields.

## 3. Job Graph

Primary Dance Sell job:

| Field | Value |
|---|---|
| Dance Sell job | `5ec35e57-4958-4991-b833-2e8c05e9debb` |
| Logical request | `dance-sell-b797952903ca42ed8b6cd32e25e19dc6` |
| Final status | `failed` |
| Provider/model | `79ai` / `kling_video_motion_3` |
| Final provider task ID | `NOT AVAILABLE` |
| Reference media | `89d3b09a-baf8-429e-bd00-4ec8f8fad159` |
| Motion media | `2bd91857-2007-482d-90c4-5773162dc79b` |
| Motion bytes | `23519199` |
| Motion MIME | `video/mp4` |

Attempts:

| Attempt | Operation | Render job | Parent operation | Status | Provider task |
|---|---|---|---|---|---|
| 1 | `d3d744f5-2847-4e24-87b3-f8801a8a716e` | `4db26687-1883-4a18-a54a-23ceaf0cc502` | `NOT AVAILABLE` | failed | `NOT AVAILABLE` |
| 2 | `c3f7e136-a1e2-493d-a2df-556d63a5035e` | `5013d57f-aa63-408f-8a0f-57cbcff0c605` | `d3d744f5-2847-4e24-87b3-f8801a8a716e` | failed | `NOT AVAILABLE` |
| 3 | `1521d481-1218-491c-a530-6137225be687` | `976685e0-66ea-4889-b94d-93ab79c02fb8` | `c3f7e136-a1e2-493d-a2df-556d63a5035e` | failed | `NOT AVAILABLE` |
| 4 | `3d75044b-5959-4e20-8b1a-41085bac7ab6` | `ad2ed483-9d92-477c-bd4a-ce382ccf2b6a` | `1521d481-1218-491c-a530-6137225be687` | failed | `NOT AVAILABLE` |

## 4. First Submit Timeline

First render: `4db26687-1883-4a18-a54a-23ceaf0cc502`

| Time UTC | Event | Evidence |
|---|---|---|
| 2026-09-17T09:03:48.9904130Z | `AI_PROVIDER_REFERENCE_UPLOAD_STARTED` | prepared reference media `89d3b09a-baf8-429e-bd00-4ec8f8fad159` |
| 2026-09-17T09:03:49.1199820Z | `AI_PROVIDER_REFERENCE_UPLOAD_FAILED` | `79AI upload file 'file' could not be opened.` |
| 2026-09-17T09:03:49.1750530Z | `AI79_MOTION_SUBMIT_FAILED` | stage `reference_upload`, error `missing_file` |
| 2026-09-17T09:03:49.4451170Z | `KIE_TASK_FAILED` | provider task ID `null` |

This attempt did not reach provider submit. It failed while preparing/uploading the reference file.

## 5. Retry Timeline

Required retry render: `5013d57f-aa63-408f-8a0f-57cbcff0c605`

| Time UTC | Event | Evidence |
|---|---|---|
| 2026-09-17T09:04:18.9285570Z | `JOB_RETRY_OF` | retry of `4db26687-1883-4a18-a54a-23ceaf0cc502` |
| 2026-09-17T09:04:19.8453020Z | reference upload started | fresh reference upload |
| 2026-09-17T09:04:23.7728400Z | reference verify completed | provider image `f22c56ee8f852d48`, status `SUCCESS` |
| 2026-09-17T09:04:23.8178090Z | reference upload completed | list_images verification matched |
| 2026-09-17T09:04:23.8530540Z | motion upload started | motion media `2bd91857-2007-482d-90c4-5773162dc79b` |
| 2026-09-17T09:04:47.0508600Z | motion verify completed | provider video `a85bfaf5eca4dc2a`, status `MEDIA_GENERATION_STATUS_SUCCESSFUL` |
| 2026-09-17T09:04:47.1666610Z | motion upload completed | list_videos verification matched |
| 2026-09-17T09:04:47.2000290Z | submit started | endpoint `/ai/jobs/video/kling_video_motion_3` |
| 2026-09-17T09:06:32.6498730Z | submit failed | stage `submit`, error `provider_error` |

Verification-to-submit delta for the required retry:

| Asset | Verification completed | Submit started | Delta |
|---|---:|---:|---:|
| Reference image | 2026-09-17T09:04:23.7728400Z | 2026-09-17T09:04:47.2000290Z | about 23.427s |
| Motion video | 2026-09-17T09:04:47.0508600Z | 2026-09-17T09:04:47.2000290Z | about 149ms |

Later retries:

| Render | Submit start | Result |
|---|---|---|
| `976685e0-66ea-4889-b94d-93ab79c02fb8` | 2026-09-17T15:55:01.2836510Z | failed with provider error `2100` |
| `ad2ed483-9d92-477c-bd4a-ce382ccf2b6a` | 2026-09-17T23:46:39.2854940Z | failed with provider error `2100` |

## 6. First vs Retry Comparison

| Field | First submit | Retry | Difference |
|---|---|---|---|
| operation_id | `d3d744f5-2847-4e24-87b3-f8801a8a716e` | `c3f7e136-a1e2-493d-a2df-556d63a5035e` | different operation |
| logical_request_id | `dance-sell-b797952903ca42ed8b6cd32e25e19dc6` | same | same |
| attempt | 1 | 2 | retry increments attempt |
| reference media_id | `89d3b09a-baf8-429e-bd00-4ec8f8fad159` | `89d3b09a-baf8-429e-bd00-4ec8f8fad159` | same |
| motion media_id | `2bd91857-2007-482d-90c4-5773162dc79b` | `2bd91857-2007-482d-90c4-5773162dc79b` | same |
| reference provider_id | `NOT AVAILABLE` | `f22c56ee8f852d48` | first did not upload |
| motion provider_id | `NOT AVAILABLE` | `a85bfaf5eca4dc2a` | first did not upload motion |
| reference provider URL | `NOT AVAILABLE` | `https://ai-cdn.gommo.net/ai/images/9c8318abfa3d29c4/f22c56ee8f852d48.png` | first did not upload |
| motion provider URL | `NOT AVAILABLE` | `https://ai-cdn.gommo.net/ai/videos/9c8318abfa3d29c4/a85bfaf5eca4dc2a.mp4` | first did not upload motion |
| reference MIME | `image/png` | `image/png` | same local media |
| motion MIME | `video/mp4` | `video/mp4` | same local media |
| reference size | `2141370` | `2141370` | same local media |
| motion size | `23519199` | `23519199` | same local media |
| reference upload completed | no | yes | retry progressed farther |
| reference verification completed | no | yes | retry progressed farther |
| motion upload completed | no | yes | retry progressed farther |
| motion verification completed | no | yes | retry progressed farther |
| submit_started_at | `NOT AVAILABLE` | 2026-09-17T09:04:47.2000290Z | first did not submit |
| submit_response_at | 2026-09-17T09:03:49.1750530Z failure event | 2026-09-17T09:06:32.6498730Z failure event | different failure stage |
| submit duration | `NOT AVAILABLE` | about 105.45s by event timestamps; provider raw runtime `105.1` | retry reached provider submit |
| provider task ID | `NOT AVAILABLE` | `NOT AVAILABLE` | no task created |
| error code | `missing_file` | `provider_error`, raw `2100` | different error |
| error message | `79AI upload file 'file' could not be opened.` | `Tải media lên thất bại, vui lòng kiểm tra file và thử lại.` | different stage/message |

## 7. Provider Upload Comparison

The required retry used the same reference and motion media as the failed first attempt, but only the retry reached provider upload/verification.

Retry provider assets:

| Role | Media | Provider ID | Provider URL | Bytes | Verification |
|---|---|---|---|---:|---|
| reference | `89d3b09a-baf8-429e-bd00-4ec8f8fad159` | `f22c56ee8f852d48` | `https://ai-cdn.gommo.net/ai/images/9c8318abfa3d29c4/f22c56ee8f852d48.png` | 2141370 | matched via `list_images` |
| motion | `2bd91857-2007-482d-90c4-5773162dc79b` | `a85bfaf5eca4dc2a` | `https://ai-cdn.gommo.net/ai/videos/9c8318abfa3d29c4/a85bfaf5eca4dc2a.mp4` | 23519199 | matched via `list_videos` |

Important: list verification completed only 149ms before submit on render `5013...`, but two later retries hours later reused or reverified the same provider motion URL and still failed. Therefore timing alone is not sufficient as root cause.

## 8. Provider Request Comparison

Required retry submit request:

| Field | Value |
|---|---|
| endpoint | `/ai/jobs/video/kling_video_motion_3` |
| method | `POST` inferred from submit client code path |
| Content-Type | `application/x-www-form-urlencoded` |
| model | `kling_video_motion_3` |
| subType | `motion` |
| mode | `standard` |
| ratio | `default` |
| projectId | `default` |
| backgroundSource | `input_video` |
| imageUrl | `https://ai-cdn.gommo.net/ai/images/9c8318abfa3d29c4/f22c56ee8f852d48.png` |
| videoUrl | `https://ai-cdn.gommo.net/ai/videos/9c8318abfa3d29c4/a85bfaf5eca4dc2a.mp4` |
| images[0].url | same as `imageUrl` |
| duration | local business request `15`; not present as a provider submit field in recorded submit object |

Comparison against successful jobs:

| Field | Failed retry `5013...` | Success comparisons | Same/Different |
|---|---|---|---|
| endpoint | `/ai/jobs/video/kling_video_motion_3` | same | Same |
| Content-Type | `application/x-www-form-urlencoded` | same | Same |
| model | `kling_video_motion_3` | same | Same |
| subType | `motion` | same | Same |
| mode | `standard` | same | Same |
| ratio | `default` | same | Same |
| projectId | `default` | same | Same |
| backgroundSource | `input_video` | same | Same |
| reference provider ID | `f22c56ee8f852d48` | one success used `f22c56ee8f852d48`; others used different IDs | Not uniquely failing |
| motion provider ID | `a85bfaf5eca4dc2a` | successes used `af4b7b41fbe90626`, `1ab57387e970a940`, `5fa6776c00e580ee` | Different |
| motion bytes | `23519199` | `3084895`, `3084895`, `1928150` | Different |

## 9. Provider Response Comparison

Failed required retry response:

| Field | Value |
|---|---|
| HTTP status | `NOT AVAILABLE` |
| success | `false` in normalized error JSON |
| raw success | `true` in provider raw body |
| raw error | `2100` |
| message | `Tải media lên thất bại, vui lòng kiểm tra file và thử lại.` |
| provider task ID | `NOT AVAILABLE` |
| provider media IDs | image `f22c56ee8f852d48`, video `a85bfaf5eca4dc2a` |

Successful comparison responses:

| Dance job | Provider task | Response |
|---|---|---|
| `9d1076b8-e288-4cec-b23d-66b137f5b99f` | `5edc5c1bd4d0f1cf` | submit success, task pending then completed |
| `bd85376f-4d73-4f96-8977-656551d053e6` | `816ee19e88659142` | submit success on attempt 2 |
| `d4f7e068-9a07-45de-911c-72c193f80eb7` | `831ee0b1e059f6f5` | submit success on attempt 2 |

## 10. Media Comparison

Failed target media:

| Media | ID | MIME | Bytes | Width | Height | Hash |
|---|---|---|---:|---|---|---|
| reference image | `89d3b09a-baf8-429e-bd00-4ec8f8fad159` | `image/png` | 2141370 | NOT AVAILABLE | NOT AVAILABLE | NOT AVAILABLE |
| motion video | `2bd91857-2007-482d-90c4-5773162dc79b` | `video/mp4` | 23519199 | NOT AVAILABLE | NOT AVAILABLE | NOT AVAILABLE |

Successful comparison motion videos were much smaller:

| Success task | Motion provider ID | Bytes |
|---|---|---:|
| `5edc5c1bd4d0f1cf` | `af4b7b41fbe90626` | 3084895 |
| `816ee19e88659142` | `1ab57387e970a940` | 3084895 |
| `831ee0b1e059f6f5` | `5fa6776c00e580ee` | 1928150 |

The database did not store codec, FPS, bitrate, audio stream, or video stream metadata for the target media.

## 11. Success Job Comparison

| Dance job | Attempts | Final status | Task | Motion bytes | Notes |
|---|---:|---|---|---:|---|
| `9d1076b8-e288-4cec-b23d-66b137f5b99f` | 1 | completed | `5edc5c1bd4d0f1cf` | 3084895 | same provider/model/contract |
| `bd85376f-4d73-4f96-8977-656551d053e6` | 2 | completed | `816ee19e88659142` | 3084895 | first operation failed, second succeeded |
| `d4f7e068-9a07-45de-911c-72c193f80eb7` | 2 | completed | `831ee0b1e059f6f5` | 1928150 | used same reference provider ID as failed target, different motion provider ID |

The `d4f7...` success is especially useful: it used reference provider image `f22c56ee8f852d48`, the same reference ID used by failed target retries. That argues against the reference image as the primary failing asset.

## 12. Code Path Correlation

Observed source path:

1. `RDanceJobDetail.razor` `ConfirmAndQueueAsync()` routes failed/timeout/cancelled jobs to `RetryAsync()`.
2. `QueueRenderFromUserActionAsync()` reloads the job and calls `DanceSell.QueueRenderAsync()`.
3. `DanceSellPhase2Service.QueueRenderAsync()` creates a motion provider operation and enqueues a render job.
4. `DanceSellPhase2Service.RetryAsync()` creates a new operation with `ParentOperationId`.
5. `DanceSellRenderHandler.Submit79AiAsync()` resolves reference and motion files, uploads/verifies provider assets, records operation assets, then calls 79AI submit.
6. `VerifyProviderImageAsync()` and `VerifyProviderVideoAsync()` verify uploaded assets through provider list APIs before submit.

This matches production evidence:

| Evidence | Code correlation |
|---|---|
| Attempt 1 fails at reference upload | `Submit79AiAsync()` catches upload exception and emits `AI_PROVIDER_REFERENCE_UPLOAD_FAILED` |
| Attempt 2 uploads and verifies both assets before submit | upload/verify blocks in `DanceSellRenderHandler` |
| Later retries reuse motion provider upload | `IsVerifiedProviderAsset` path emits `AI79_MOTION_SOURCE_UPLOAD_REUSED` |
| Submit failure produces no provider task | `SubmitMotionControlAsync` throws before `MarkSubmittedAsync` |

## 13. Commit Timeline

The requested regression comparison cannot prove a code regression from DB evidence alone.

What production evidence supports:

- Current code path submits immediately after list verification.
- The same submit contract can succeed for other jobs.
- The target job's same provider motion video ID failed repeatedly, including hours after initial upload.

What production evidence does not support:

- No evidence that commit `42f6065` changed the effective submit payload for the target job.
- No evidence that a successful retry exists for `5ec35e57...`.
- No evidence that `35184eea` directly caused provider error `2100`.

## 14. Root Cause

Classification: `F. JOB / INPUT SPECIFIC`

Proven root cause for the first attempt:

- Reference upload file-open failure: `79AI upload file 'file' could not be opened.`

Most supported root cause for the required retry and later retries:

- The target motion provider asset `a85bfaf5eca4dc2a` / source media `2bd91857-2007-482d-90c4-5773162dc79b` was accepted by upload/list verification but rejected by 79AI/Kling submit with raw error `2100` and no task creation.

This is job/input-specific because other jobs used the same provider/model/endpoint/contract successfully, and one success used the same reference provider image ID but a different motion provider video ID.

## 15. Evidence Supporting Root Cause

- Four operations exist for the target job; all failed.
- Attempt 1 failed at `reference_upload` with `missing_file`, before submit.
- Attempt 2, required render `5013...`, verified reference and motion assets then failed at provider submit with raw error `2100`.
- Attempts 3 and 4 reused or reverified the same motion provider URL and failed with the same provider submit error.
- No target attempt has a provider task ID.
- Success jobs show the same provider/model/endpoint/content-type can create tasks.
- Success `d4f7...` used the same reference provider image ID `f22c56ee8f852d48`, reducing likelihood that the reference image caused the submit failure.
- Failed target motion video size was `23519199` bytes; success comparison motion files were `3084895`, `3084895`, and `1928150` bytes.

## 16. Evidence Against Alternative Causes

| Alternative | Assessment |
|---|---|
| Code regression | UNPROVEN. Same provider contract succeeds for comparison jobs. |
| Reference asset lifecycle | Unlikely for submit-stage failures because success `d4f7...` used the same reference provider ID. |
| Timing/race only | Unproven. Attempt 2 submitted 149ms after motion verification, but attempts 3/4 failed hours later with the same motion provider URL. |
| Provider-wide outage | Unlikely. Success jobs completed on the same provider/model around the same period. |
| Missing provider credentials | Unlikely. Upload/list verification and success comparisons worked. |
| Public URL inaccessible at current time | Not tested and not needed for root cause; provider CDN URLs existed in provider list evidence at submit time. |

## 17. Confidence

Confidence: MEDIUM

Reason: Production DB gives a complete job/operation/render-event chain and raw provider error for the required job. Confidence is not HIGH because the DB does not store detailed video codec/FPS/bitrate/audio metadata, and we did not perform provider-side or media-file downloads due the read-only/no-provider-call constraint.

## 18. Recommended Fix

Do not treat list verification alone as sufficient for Kling Motion submit readiness or compatibility.

Recommended next implementation, not performed in this audit:

1. For 79AI/Kling Motion submit error `2100`, persist a structured failure category such as `provider_media_submit_rejected`.
2. Add pre-submit media validation for motion files: size, duration, codec/container, resolution, FPS, audio stream, and provider model limits.
3. If 79AI exposes a stronger media-readiness API than list_images/list_videos, use it before submit.
4. Add a bounded same-operation retry/backoff only for ambiguous media-ingest timing, but stop retrying when the same provider video ID repeatedly returns `2100`.
5. Surface a user-facing recovery path that asks for a different motion video when the same motion provider ID fails multiple submit attempts.

## Validation

- Production extractor: passed; connected to `TodoXSaaS`, executed SELECT-only queries, wrote valid JSON.
- Credential-value scan: passed; no bearer/API-key/secret values found in evidence.
- `dotnet build TodoX.Web.csproj --no-restore`: passed with 0 errors and 45 existing compiler warnings in generated Razor code.
- `dotnet format TodoX.Web.csproj whitespace --verify-no-changes --no-restore`: failed because the existing worktree contains unrelated whitespace violations, including existing files under `Services/DanceSell`; no formatter changes were applied.
- Tests: not run because this was a read-only production DB audit and no production code was changed.
- Publish/deploy: intentionally not run; prohibited by this audit scope.

## Final Output

STATUS: COMPLETED

ROOT CAUSE: JOB / INPUT SPECIFIC. First attempt failed opening the reference upload file; required retry and later retries failed at 79AI submit with raw error `2100` for the same motion provider video asset.

CONFIDENCE: MEDIUM

PRODUCTION_DB_ACCESS: YES

FIRST_SUBMIT_EVIDENCE: FOUND

RETRY_EVIDENCE: FOUND

FIRST_VS_RETRY_DIFFERENCE: first attempt failed at reference upload before submit; retry uploaded and verified both assets, then failed at provider submit with error `2100`. No successful retry for this job was found.

CODE_REGRESSION: UNPROVEN

ASSET_LIFECYCLE: UNPROVEN

PROVIDER_ISSUE: UNPROVEN

TIMING_RACE: UNPROVEN

RECOMMENDED_FIX: add structured handling for 79AI error `2100`, validate motion media compatibility before submit, and ask for a replacement motion video after repeated same-asset submit failures.

FILES_CHANGED: NONE

DB_CHANGED: NO

PROVIDER_TASK_CREATED: NO

DEPLOYED: NO

# END
