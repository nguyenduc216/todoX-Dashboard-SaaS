# RDance First Submit Failure Root Cause Audit

Audit date: 2026-09-17

Repository: `nguyenduc216/todoX-Dashboard-SaaS`

Audited commit: `42f606538a1e6b807adb965f08efafeedc90d07e`

Mode: read-only audit. No production code was changed, no database writes were made, no provider task was created, no deployment was performed, and no commit/push was performed.

## 1. Executive Summary

The requested production root cause cannot be proven from this workspace because the production database connection strings are not available and no local evidence file contains the failed/retry job `5ec35e57-4958-4991-b833-2e8c05e9debb` or render job `5013d57f-aa63-408f-8a0f-57cbcff0c605`.

Code audit found a plausible mechanism already present in the RDance render path: provider media uploads are considered verified after provider list verification, then submit runs immediately. Retry can reuse persisted verified provider assets. That mechanism can make retry differ from first submit if 79AI/Kling media ingest readiness lags behind list visibility. However, this is only an inference until the actual first/retry operation rows, event timestamps, request JSON, response JSON, provider media IDs, and asset metadata are read from production.

The audit therefore classifies the issue as `H. Insufficient evidence`.

## 2. Classification

Selected classification: `H. Insufficient evidence`

Reasons:

- No production DB access was available in the current environment.
- No local evidence exists for the failed/retry job IDs specified in the task.
- Code path can explain how first submit and retry may differ, but the actual job data is required to prove whether that happened.
- The current commit `42f6065` mostly changes UI state/polling plus an optional multipart path that RDance render does not currently pass into; it does not by itself prove a first-submit provider failure root cause.

## 3. Evidence

| Evidence | Source | Result | Classification |
|---|---|---|---|
| Current HEAD is `42f606538a1e6b807adb965f08efafeedc90d07e` | `git rev-parse HEAD` | confirmed | FACT |
| Worktree has pre-existing untracked `docs/reports/rdance-video-flow-fix-2026-09-17.md` | `git status --short` | untracked before this audit | FACT |
| No `ConnectionStrings__TodoXSaaS` / `ConnectionStrings__TodoXAutomation` env vars | PowerShell env lookup | no values available | FACT |
| Local search found `d4f7e068...` only in untracked report, not DB evidence | `rg` over repo | no evidence for `5ec35e57...` | FACT |
| `QueueRenderFromUserActionAsync` reloads job before queueing | `RDanceJobDetail.razor` | reduces stale `_job` risk before first submit | FACT |
| Retry UI calls `DanceSell.RetryAsync` for failed/timeout/cancelled job | `RDanceJobDetail.razor` | retry is explicit customer action | FACT |
| `QueueRenderAsync` creates provider operation and render job | `DanceSellPhase2Services.cs` | first submit has new operation/render job | FACT |
| `RetryAsync` creates a new operation with parent operation | `DanceSellPhase2Services.cs` | retry is distinct operation | FACT |
| Motion asset lookup can reuse prior verified provider upload | `DanceSellRenderHandler.cs` | retry can submit with already persisted provider asset | FACT |
| Submit uses form-urlencoded by default | `Ai79TaskClient.SubmitMotionControlAsync` | effective current RDance path sends URLs, not file parts | FACT |
| Optional multipart path in `42f6065` is not populated by current render handler | constructor call in `DanceSellRenderHandler.cs` | no proven payload change for RDance first submit | OBSERVATION |

## 4. Timeline

### First submit

Production timeline for `5ec35e57-4958-4991-b833-2e8c05e9debb` / `5013d57f-aa63-408f-8a0f-57cbcff0c605` could not be reconstructed without DB access.

Expected code path:

1. Customer clicks `Tạo video`.
2. `ConfirmAndQueueAsync()`
3. `QueueRenderFromUserActionAsync()`
4. `DanceSell.GetAsync()` reloads job.
5. Approved reference check.
6. `DanceSell.QueueRenderAsync()`
7. Provider operation row created.
8. Render job created.
9. Worker claims render job.
10. `DanceSellRenderHandler.Submit79AiAsync()`
11. Resolve local/media/public files.
12. Upload/verify reference image if not reusable.
13. Upload/verify motion video if not reusable.
14. Build form-url-encoded request.
15. `Ai79TaskClient.SubmitMotionControlAsync()`.

### Retry

Production retry timeline for the failed job could not be reconstructed.

Expected code path:

1. Customer clicks `Tạo video` on failed/timeout state.
2. `ConfirmAndQueueAsync()` calls `RetryAsync()`.
3. `DanceSell.RetryAsync()` creates a new operation with `ParentOperationId`.
4. New render job/attempt is queued.
5. Render handler can reuse verified provider assets from previous operation if media ID/object key match.
6. Submit runs again.

### Successful job

Local report references successful job:

- Dance Sell: `d4f7e068-9a07-45de-911c-72c193f80eb7`
- Render: `d200bd0d-901f-407b-847b-4a9688b426f8`
- Provider task: `831ee0b1e059f6f5`

This is not sufficient to compare first/retry for the required failed job because it is a different job and the raw DB events/request/response rows are not available here.

## 5. First vs Retry Comparison

Required production comparison is not available from this workspace.

| Field | First submit | Retry submit | Difference |
|---|---|---|---|
| operation ID | not verified | not verified | production DB required |
| render job ID | `5013d57f-aa63-408f-8a0f-57cbcff0c605` per task | not verified | production DB required |
| attempt number | not verified | not verified | production DB required |
| logical request ID | not verified | not verified | production DB required |
| provider code | not verified | not verified | production DB required |
| model | not verified | not verified | production DB required |
| provider task ID | likely null on failed submit, not verified | not verified | production DB required |
| reference provider ID | not verified | not verified | production DB required |
| motion provider ID | not verified | not verified | production DB required |
| provider URLs | not verified | not verified | production DB required |
| request JSON | not verified | not verified | production DB required |
| response JSON | not verified | not verified | production DB required |
| timestamps | not verified | not verified | production DB required |
| error code/message | not verified | not verified | production DB required |
| file metadata | not verified | not verified | production DB/media evidence required |

Conclusion: first-vs-retry difference is unproven.

## 6. Provider Request Comparison

Code-level effective submit contract for current RDance URL-based path:

| Field | Source in code | Effective value |
|---|---|---|
| endpoint | runtime route | `/ai/jobs/video/kling_video_motion_3` expected by route/config |
| HTTP method | `Ai79TaskClient` | `POST` |
| Content-Type | `FormUrlEncodedContent` | `application/x-www-form-urlencoded` |
| auth | `Authorization: Bearer` | token is not exposed |
| `domain` | runtime | from provider route/account |
| `project_id` | runtime | from provider route/account |
| `model` | runtime | provider model |
| `prompt` | runtime route config | `motion_prompt` or empty |
| `image_url` | render handler | `referenceUrlUsed` |
| `video_url` | render handler | `motionProviderUrl` |
| `images[0][url]` | request flag | same as `referenceUrlUsed` when enabled |
| `subType` | runtime | route/config |
| `background_source` | runtime | route/config |
| `mode` | runtime | provider mode |
| `ratio` | runtime | provider ratio |

Important code finding:

- `42f6065` added optional multipart fields to `Ai79MotionControlSubmitRequest`.
- `DanceSellRenderHandler` currently constructs `Ai79MotionControlSubmitRequest` without `CharacterImageFile` and `MotionVideoFile`.
- Therefore, the audited current RDance render path still uses the form-url-encoded URL submit path unless another caller supplies file parts.

Actual first-vs-retry payload equality cannot be proven without DB `request_json` / operation rows.

## 7. Media Comparison

Actual media metadata for failed first submit, retry success, and success cases is not available in this workspace.

Required but not available:

- image bytes/MIME/width/height/hash
- video bytes/MIME/container/codec/width/height/FPS/duration/bitrate/audio streams/hash
- provider media ID
- provider upload URL
- verification matched/download URL
- upload timestamp
- verification timestamp
- submit timestamp

Code-level metadata persisted when upload succeeds:

- `mime`
- `bytes`
- provider `idBase`
- `uploadUrl`
- `verificationMatchedUrl`
- `verificationDownloadUrl`
- `providerStatus`
- `verificationMatched = true`
- `uploadedAt`
- upload endpoint/field

Note: current HTTP accessibility of a URL would not prove accessibility at submit time.

## 8. Code Path

Call graph:

```text
RDanceJobDetail.razor
  ConfirmAndQueueAsync()
    if failed/timeout/cancelled -> RetryAsync()
    else -> QueueRenderFromUserActionAsync()

QueueRenderFromUserActionAsync()
  UpdateBusinessAsync()
  DanceSell.GetAsync()
  validate PreparedReferenceStatus == Approved
  DanceSell.QueueRenderAsync()
  ReloadAsync()
  StartPolling()

DanceSellPhase2Service.QueueRenderAsync()
  RequireOwnedJobAsync()
  ValidateReadyForRender()
  Resolve motion route/provider mode
  Resolve duration/pricing
  Charge wallet
  Upsert provider operation
  Enqueue render job
  QueueForRenderAsync()

RenderJobWorker
  Claim job
  Mark Rendering
  Dispatch to DanceSellRenderHandler

DanceSellRenderHandler.Submit79AiAsync()
  Resolve runtime
  Resolve reference file
  Resolve motion file
  Ensure motion operation
  Reuse or upload provider reference asset
  Reuse or upload provider motion asset
  Verify provider assets via provider list APIs
  Build Ai79MotionControlSubmitRequest
  BeginMotionSubmitAttemptAsync()
  Add AI79_MOTION_SUBMIT_STARTED event
  Ai79TaskClient.SubmitMotionControlAsync()
  MarkSubmittedAsync()
  Schedule poll

DanceSellRenderHandler.Poll79AiAsync()
  GetStatusAsync()
  on success with output -> DanceSellCompletionService.CompleteAsync()

DanceSellCompletionService.CompleteAsync()
  UpdateCompletedAsync()
  Mark render job completed
  Add completion event
```

Retry-specific path:

```text
RDanceJobDetail.RetryAsync()
  DanceSell.RetryAsync()
    lock per dance job
    require failed/timeout/cancelled or retryable state
    create new provider operation with ParentOperationId
    enqueue/prepare retry render
```

Potential difference visible in code:

- First attempt may freshly upload and verify assets immediately before submit.
- Retry may reuse provider asset rows whose metadata already has `verificationMatched = true`.
- This can change `upload -> verify -> submit` timing without changing effective submit payload.

This is a hypothesis until DB timestamps prove it.

## 9. Git Timeline

### `294d96c5` - `fix(rdance): persist video duration`

Files:

- `DanceSellAiOperations.cs`
- `DanceSellRepository.cs`
- tests/report

Behavior:

- Merges submit request JSON into existing job/operation request JSON instead of replacing it.
- Preserves duration and other request metadata.

Provider upload/request contract impact:

- No direct provider upload change found.
- No direct 79AI submit contract change found.
- Duration is used for validation/pricing; it is not sent as a submit field in `Ai79TaskClient.SubmitMotionControlAsync()`.

### `35184eea` - `fix(rdance): recover reference upload from public media url`

Files:

- `DanceSellRenderHandler.cs`
- test file

Behavior:

- If `_media.OpenReadAsync(mediaId)` returns null, the render handler can recover media bytes from a configured/public HTTPS URL.
- Adds `IConfiguration` dependency to build recoverable public media URLs.

Provider upload/request contract impact:

- Can affect whether the handler can obtain bytes for provider upload.
- Does not directly change the 79AI Kling submit fields.
- If recovery changed the actual bytes uploaded, DB/media metadata would be needed to prove impact.

Assessment for first-submit failure:

- If first submit failure happened after provider upload and verification, this commit alone is not proven as root cause.
- If first submit uploaded different/recovered bytes than retry, production asset metadata is required to prove it.

### `42f6065` - `fix(rdance): stabilize video creation flow`

Files:

- `RDanceJobCreate.razor`
- `RDanceJobDetail.razor`
- `Ai79TaskClient.cs`
- regression test/report

Behavior:

- Adds image/motion loading states and immediate UI paint before awaits.
- Adds centered empty result state.
- Starts/continues UI polling more robustly.
- Adds optional multipart branch in `Ai79TaskClient`.

Provider upload/request contract impact:

- RDance render handler currently does not pass multipart file parts to `Ai79MotionControlSubmitRequest`.
- Effective RDance submit remains form-url-encoded URL-based in the audited call graph.
- UI state changes do not by themselves create duplicate queue calls because `_busy` gates `RunAsync`, `QueueRenderFromUserActionAsync` reloads job, and backend rejects already active jobs.

Assessment:

- No code-only proof that `42f6065` causes first-submit provider failure.
- It changes post-queue polling/UI behavior, not provider asset upload/verification order in `DanceSellRenderHandler`.

## 10. Root Cause

Proven root cause: not established.

What evidence supports:

- The code path allows first submit and retry to differ in asset lifecycle timing.
- First attempt can freshly upload/verify provider media immediately before submit.
- Retry can reuse already persisted verified provider media.
- The submit payload may remain effectively the same while provider-side media readiness differs.

What evidence is missing:

- Whether the failed first submit actually used freshly uploaded assets.
- Whether retry reused the same provider media IDs/URLs or re-uploaded.
- Whether first and retry payloads were identical.
- Provider response JSON/error message for the first failure.
- Upload/verify/submit timestamps for the failed job.

Conclusion:

`H. Insufficient evidence` is the only defensible classification.

## 11. What Is NOT the Root Cause

Not proven as root cause:

- `42f6065` UI loading changes.
- Dashboard polling changes.
- Result empty-state layout.
- Optional multipart branch in `Ai79TaskClient`, because current RDance render handler does not pass file parts.
- `294d96c5` duration persistence, because duration is not an effective 79AI submit field in the audited client method.

Not ruled out:

- Provider asset readiness race after list verification.
- Job/input-specific media issue.
- Provider-side transient/media ingest failure.
- Difference between first and retry provider media IDs/URLs.
- Recovery-from-public-URL changing uploaded bytes for a specific job.

## 12. Recommended Fix

Do not implement a code fix until production evidence is collected.

Recommended next read-only actions:

1. Query production DB for the specified job/render IDs.
2. Export chronological `render_job_events`.
3. Export `dance_sell_provider_operations` for the job, including request/response/error JSON.
4. Export `public.todox_ai_operation_assets` joined by operation ID.
5. Compare first and retry provider media IDs, upload URLs, verification URLs, timestamps, MIME, bytes, media IDs, object keys, and submit payloads.
6. If evidence proves provider media readiness lag, add a contract-based readiness check if 79AI exposes one; otherwise add bounded idempotent submit retry tied to the same logical request/provider assets, not duplicate render jobs.
7. If evidence proves payload mismatch, fix the exact field mapping.
8. If evidence proves recovered/local media mismatch, fix media resolution and checksum/metadata persistence.

Suggested read-only query targets:

- `dance_sell.dance_sell_jobs`
- `render.render_jobs`
- `render.render_job_events`
- `dance_sell.dance_sell_provider_operations`
- `public.todox_ai_operation_assets`
- reference version/media tables discovered from repository schema
- media table(s) used by `IMediaFileService`

## 13. Confidence

Confidence: Low

Reason:

- Code path confidence is medium because the relevant source was audited.
- Root cause confidence is low because the required production first/retry data is unavailable.
- No production operation rows, event timestamps, provider responses, or media metadata were accessible from this workspace.

## Validation

- Production code changed: NO
- Database changed: NO
- Provider task created: NO
- Deployed: NO
- Commit/push performed: NO
- Report exists: YES
- Git status before report showed only pre-existing untracked `docs/reports/rdance-video-flow-fix-2026-09-17.md`
- Report path is under ignored `artifacts/`, so normal `git status` does not show it unless force-added.
