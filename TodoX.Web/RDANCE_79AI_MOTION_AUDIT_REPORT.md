# RDance / dance_sell 79AI Kling Motion Audit

Audit date: 2026-09-22

Scope: static and forensic audit of the RDance / `dance_sell` path for 79AI model `kling_video_motion_3`. This report does not audit RVIDEO, Prompt Assistant, or `RenderVideoJobs.razor`.

Evidence limitation: the supplied production IDs were not found in the repository's local evidence artifacts, and the original image/video bytes were not present in the workspace. Therefore source-level findings are confirmed, while job-specific timelines and media comparisons are marked UNKNOWN where no read-only runtime export is available.

## 1. Executive Summary

The Dashboard RDance path is a distinct `dance_sell` flow. It prepares an approved reference image, resolves the motion source, uploads both media to 79AI, verifies each through provider list endpoints, constructs a URL-based form request, submits `kling_video_motion_3`, persists the provider task, polls it, and maps terminal status.

The exact wire submit contract is CONFIRMED from source: HTTP `POST`, `application/x-www-form-urlencoded`, bearer authentication, and fields `domain`, `project_id`, `model`, `prompt`, `image_url`, `video_url`, `subType`, `background_source`, `mode`, `ratio`, plus optional `images[0][url]`. The internal `request_json` is an audit/persistence object and is not the serialized wire body.

The success job `49554a92-900a-42ec-8f81-543faa0bfe95` and failed job `28171031-d86d-4c67-8eff-67aab154df5f` cannot be compared at the database-row or media-byte level from this workspace. The two failed render attempts must remain separate: attempt `7962f638-8e2a-4ad4-9ce3-3d17ce2f3492` is described as submit failure, while `3fe09576-48b5-4e02-ba00-c89ad4a0572f` created provider task `56a3fb0578396662` and later failed during polling.

The code contains a real retry branch: a new provider operation/render job can reuse a prior verified provider asset only after live verification. A reuse mismatch causes `AI_PROVIDER_REFERENCE_ASSET_REUSE_REJECTED` and a fresh upload. This is a confirmed mechanism, not proof that it caused either supplied job failure.

Confirmed observability defect: the poll failure path calls `FailAsync` with `danceJob.ProviderStatus`, which can still contain the previous `RUNNING` value, instead of the just-returned terminal provider status. This can make a `KIE_TASK_FAILED`/failure event show `providerStatus=RUNNING` while the actual poll response is `ERROR`. The persisted job is still marked failed through the completion service, so this is primarily stale diagnostic data unless downstream consumers rely on the stale provider-status field.

Root cause for the supplied jobs: UNKNOWN. DO NOT PATCH YET.

## 2. Files / Classes / Methods Inspected

- `Services/DanceSell/DanceSellRenderHandler.cs`
  - `HandleAsync`
  - `Submit79AiAsync`
  - `Poll79AiAsync`
  - `Resolve79AiRuntimeAsync`
  - `ReverifyPreviousReferenceAssetAsync`
  - `VerifyProviderImageAsync`
  - `VerifyProviderVideoAsync`
  - `FailAsync`
- `Services/DanceSell/DanceSellPhase2Services.cs`
  - `QueueRenderAsync`
  - `RetryAsync`
  - render readiness and operation creation paths
- `Services/DanceSell/DanceSellRepository.cs`
  - `QueueForRenderAsync`
  - `ResetMotionRenderStateAsync`
  - `UpdateSubmittedAsync`
  - `UpdatePollingAsync`
  - `UpdateCompletedAsync`
  - `UpdateFailedAsync`
  - `SelectSql`
- `Services/DanceSell/DanceSellAiOperations.cs`
  - operation and asset persistence, submit-attempt persistence
- `Services/DanceSell/DanceSellCompletionService.cs`
  - terminal completion/failure mapping
- `Services/AiProviders/Ai79TaskClient.cs`
  - `SubmitMotionControlAsync`
  - media upload/list verification
  - `GetStatusAsync`
  - `Ai79TaskStatusNormalizer`
- `Services/AiProviders/Kie/KiePayloadBuilder.cs` and KIE models for the separate KIE path
- `Tests/RDance*` regression tests
- Existing read-only artifacts under `artifacts/codex/rdance-*` and RDance reports under `docs/`

No n8n workflow source for `todoX_rdance_02_prepare_source` or `todoX_rdance_04_submit_motion` was found in this repository workspace.

## 3. Actual RDance Architecture

```text
RDance UI/API
  -> DanceSellPhase2Service.QueueRenderAsync / RetryAsync
  -> dance_sell provider operation + render.render_jobs
  -> RenderJobDispatcher
  -> DanceSellRenderHandler.HandleAsync
  -> Submit79AiAsync
       -> resolve reference and motion local files
       -> reuse or upload reference image
       -> list-images verification
       -> reuse or upload motion video
       -> list-videos verification
       -> construct Ai79MotionControlSubmitRequest
       -> Ai79TaskClient.SubmitMotionControlAsync
       -> persist task id and submit response
  -> Poll79AiAsync
       -> Ai79TaskClient.GetStatusAsync
       -> completion or failure mapping
```

The effective 79AI path uses URL fields. The optional multipart fields in the client are not populated by the current RDance render handler constructor call.

## 4. SUCCESS Job Forensic Timeline

Target job: `49554a92-900a-42ec-8f81-543faa0bfe95`

Known supplied facts:

- Render job: `c95e0823-32e7-4e27-8752-9ac9bf73a607`
- Provider task: `842d3440df5430b8`
- Local reference: `c5c2fcb868a142e087b348bff58a178d.png`
- Provider reference: `20488258f158151a.png`
- Local motion: `motion-tiktok-8c8036a6889e448fb73741c5bfa05696.mp4`
- Provider motion: `90dd3ceb022cbff0.mp4`
- Flow: reference upload/verify -> motion upload/verify -> submit -> RUNNING -> SUCCESS

Database event timestamps, operation JSON, provider response JSON, asset metadata, and byte-level media metadata were not available locally. The stated timeline is therefore task-supplied evidence; exact row values are UNKNOWN.

Classification: HIGH CONFIDENCE for the code path shape; UNKNOWN for exact persisted values.

## 5. FAILED Job Forensic Timeline

Target job: `28171031-d86d-4c67-8eff-67aab154df5f`

Known supplied facts:

- Local reference: `1cdb72e1247c45ba938d698be671e25b.png`
- Local motion: `motion-tiktok-bb7bf0fbdb984e4eaa0f3241b4f253fc.mp4`

### Attempt `7962f638-8e2a-4ad4-9ce3-3d17ce2f3492`

Task-supplied result: failed during submit after local/provider upload and verification. No provider task ID is supplied for this attempt.

The application branch for a submit-stage exception is `Submit79AiAsync` -> `FailAsync`; if no provider task was returned, `UpdateSubmittedAsync` is not reached. Exact provider response and exact wire payload require the attempt's persisted operation/request/response rows and are UNKNOWN here.

### Attempt `3fe09576-48b5-4e02-ba00-c89ad4a0572f`

Task-supplied result: reference and motion upload/verification succeeded, submit created provider task `56a3fb0578396662`, polling observed RUNNING, then provider returned ERROR. This is a provider-task/poll failure, not a local upload failure.

The application branch is `Submit79AiAsync` -> `UpdateSubmittedAsync` -> scheduled `Poll79AiAsync` -> `GetStatusAsync` -> normalized `FAILED` -> `FailAsync`. Exact poll JSON and event rows are UNKNOWN locally.

These attempts are distinct failure modes and must not be merged.

## 6. Exact SUCCESS vs FAILED Differences

| Stage | SUCCESS | FAILED | Significance | Evidence |
|---|---|---|---|---|
| Job | `49554a92-900a-42ec-8f81-543faa0bfe95` | `28171031-d86d-4c67-8eff-67aab154df5f` | Different input/job identity | Task-supplied IDs |
| Model | `kling_video_motion_3` | `kling_video_motion_3` | No model difference shown | Task-supplied configuration |
| Reference format | PNG | PNG | PNG alone is not supported as root cause | Task-supplied comparison |
| Render attempts | One supplied render | At least two supplied renders | Retry state can change asset reuse/timing | Task-supplied IDs; source retry branch |
| Attempt 1 | Provider media verified, then submit failure | Provider media verified, then submit failure | Upload success does not prove submit acceptance | Task-supplied timeline |
| Attempt 2 | Not applicable | Provider task `56a3fb0578396662` then ERROR | Separate provider processing failure | Task-supplied timeline |
| Asset reuse | Unknown for success | Unknown per attempt; code can reverify/reuse | Requires operation asset rows | Source confirmed, runtime unknown |
| Wire payload | URL form body | URL form body unless another caller supplies multipart | Same code contract likely | `Ai79TaskClient` + handler |
| Prompt | Task says broadly same; exact current DB value unavailable | Task says broadly same; exact current DB value unavailable | Do not infer from internal JSON alone | Task-supplied statement |
| Media bytes | Not locally available | Not locally available | Cannot compare decode/codec properties | Workspace scan |

The supplied facts establish that the jobs do not execute different model/provider code. They do not establish that the media bytes, provider IDs, timestamps, or exact wire values are equal.

## 7. Exact 79AI Wire Payload Construction

### URL-based path actually called by RDance

`DanceSellRenderHandler.Submit79AiAsync` constructs `Ai79MotionControlSubmitRequest` without file parts. `Ai79TaskClient.SubmitMotionControlAsync` therefore creates:

```text
POST {runtime.BaseUrl}{runtime.MotionSubmitPath}
Authorization: Bearer <credential>
Content-Type: application/x-www-form-urlencoded

domain=<runtime.Domain>
project_id=<runtime.ProjectId>
model=<runtime.Model>
prompt=<request.Prompt>
image_url=<referenceUrlUsed>
video_url=<motionProviderUrl>
subType=<runtime.SubType>
background_source=<runtime.BackgroundSource>
mode=<runtime.ProviderMode>
ratio=<runtime.ProviderRatio>
images[0][url]=<referenceUrlUsed>   (when IncludeImagesZeroUrl is true)
```

The values observed in the supplied task context are `mode=standard`, `ratio=default`, `subType=motion`, and `prompt=""` for the audited historical requests. Current source now has prompt fallback changes from the previous task, but this audit does not use that as evidence of historical wire payloads.

### Internal `request_json`

The 79AI branch persists a JSON object containing provider/model, endpoint path, content type, reference/motion upload metadata, a `submit` object, prompt, submit attempt, and `auditPrompt`. This is diagnostic persistence; it is not serialized and sent as the HTTP body.

The KIE branch is separate and sends a JSON task payload with `model`, callback URL, and `input.prompt`, `input_urls`, `video_urls`, mode, and orientation. It is not the effective 79AI path for the supplied jobs.

Classification: CONFIRMED at source level; historical per-job values UNKNOWN.

## 8. Media Upload / Verification Findings

Reference upload uses `UploadMediaAsync`, then `ListImagesAsync`. Matching accepts provider `IdBase` or URL, and requires normalized provider status `SUCCESS`.

Motion upload uses `UploadMediaAsync`, then `ListVideosAsync`. Matching accepts `IdBase`, URL, or download URL, requires an HTTPS verified URL, and normalized status `SUCCESS`.

For a prior-attempt reference asset, `ReverifyPreviousReferenceAssetAsync` rechecks the stored provider URL and ID. `AI_PROVIDER_REFERENCE_ASSET_REUSE_REJECTED` means one of two code-level conditions:

1. The live list verification returned a different `IdBase` or URL than the stored values; or
2. Live verification threw a provider submit/list exception.

On either result the caller returns false and performs a fresh binary upload. A new upload may return the same provider `idBase` because provider identity can be content-derived, deduplicated, reused, eventually consistent, or simply allocated from the same provider-side media record. The application does not enforce uniqueness across uploads and does not prove byte identity from `idBase` alone.

The verification URL versus submit URL distinction is also confirmed as a possible representation difference: the list response may expose a URL such as `*.png?full=1`, while the submit uses the upload URL or canonical `Url` without that query. The success job reportedly exhibits the same pattern, so this is not a proven root cause.

The code verifies list visibility/status, not provider-side readiness for Kling inference. A list-visible asset can still be rejected later; that remains a HIGH CONFIDENCE hypothesis, not a confirmed cause for these IDs.

## 9. Retry / Attempt State Findings

`RetryAsync` creates a new motion provider operation with a new attempt number and `ParentOperationId`, and queues a new render job. The handler can reuse verified assets only when media identity keys match and prior assets pass live verification; otherwise it uploads fresh media.

`dance_sell_jobs` is a current-job summary, not a complete attempt ledger. The job row is reset on queue/retry for fields such as `provider_task_id`, `provider_status`, submit response, poll response, submitted timestamp, and polling counters. The provider operation rows and `render_job_events` are the per-attempt evidence.

`UpdateSubmittedAsync` uses `COALESCE(provider_task_id, @providerTaskId)` and `COALESCE(submitted_at, now())`, and merges request JSON. This is appropriate for preserving a current submit once accepted, but it means the job row alone cannot reconstruct every historical attempt. A failed submit before task creation leaves no new successful task ID; later retry can replace the current summary fields after reset.

Classification: CONFIRMED source behavior. Exact stale/mixed values for the supplied job are UNKNOWN without DB rows.

## 10. n8n vs Dashboard Differences

No actual n8n workflow files or exported workflow JSON for `todoX_rdance_02_prepare_source` or `todoX_rdance_04_submit_motion` were found in this repository workspace. Therefore no evidence-backed n8n-vs-Dashboard comparison can be made.

Dashboard behavior confirmed from source:

- source media is resolved from local/media storage;
- provider upload is URL/form based;
- upload result is list-verified before submit;
- submit sends provider media URLs and optional `images[0][url]`;
- poll uses provider task ID and normalizes status.

The stated n8n observation that one character image can be used by both failed and delivered jobs is compatible with provider-side asset reuse/deduplication, but it is external task-supplied context and does not prove a Dashboard upload failure.

## 11. Media Technical Comparison

The actual source media files for the specified success and failed jobs were not available in the repository/workspace scan. No read-only download or DB storage access was available in this turn.

Consequently the following are UNKNOWN for both jobs:

- image MIME/container, dimensions, channels, alpha, bit depth, profile, metadata, file size, and decode validity;
- MP4 duration, codec, profile, resolution, FPS, pixel format, time base, bitrate, audio, rotation, start time, stream count, and decode errors;
- hashes and equality of local versus provider media;
- whether the failed motion file differs materially from the success motion file.

PNG itself is not a supported root-cause conclusion because the success job also uses PNG.

## 12. Observability Bugs

The poll path receives a fresh `Ai79TaskStatusResult` and correctly branches on normalized `FAILED`. It calls `FailAsync` with `status.ErrorCode`, `status.ErrorMessage`, and the sanitized poll response, but `FailAsync` populates the failure request's `ProviderStatus` from `danceJob.ProviderStatus` rather than from the fresh `status.ProviderStatus` or normalized status.

Therefore a previous persisted `RUNNING` value can appear in failure event/usage metadata even when the current provider response says `ERROR`. This is CONFIRMED as a stale logging/state-input bug in source. The failure still calls the completion service, which updates the job to failed and stores the response/error JSON. The defect is primarily diagnostic and can mislead operators; it does not by itself change provider task state.

The first submit failure has a separate mapping issue: the customer-safe message is generated from the submit stage and the provider exception, while no task ID is persisted if provider submission fails before `UpdateSubmittedAsync`. That is expected control flow, not proof of a local upload failure.

## 13. Root Cause Assessment

| Finding | Classification | Assessment |
|---|---|---|
| Success and failed jobs use the same RDance/79AI model path | CONFIRMED | No source branch indicates a different provider implementation for these model values |
| PNG is the root cause | REJECTED | Success job also uses PNG; no media-byte evidence |
| Attempt 1 and attempt 2 are the same failure | REJECTED | One fails at submit; the other creates task and fails during poll |
| Internal `request_json` is the wire payload | REJECTED | Client sends form-urlencoded fields, not the persisted JSON object |
| Upload list verification proves inference readiness | REJECTED | Source proves list/status visibility only |
| Provider media readiness race | HIGH CONFIDENCE hypothesis | Fits upload/verify/submit timing, but no timestamps/provider evidence for target jobs |
| Job/input-specific motion asset issue | HIGH CONFIDENCE hypothesis | Fits distinct motion IDs/files, but media bytes unavailable |
| Stale provider status in failure diagnostics | CONFIRMED | Fresh poll status is not passed into `FailAsync` |
| Exact root cause for target jobs | UNKNOWN | Required DB/provider/media evidence is unavailable |

## 14. A/B Diagnostic Proposal

Do not execute C/D.

| Variant | Reference | Motion | Purpose |
|---|---|---|---|
| A | success reference `c5c2fcb868a142e087b348bff58a178d.png` | success motion `motion-tiktok-8c8036a6889e448fb73741c5bfa05696.mp4` | known-success control |
| B | failed reference `1cdb72e1247c45ba938d698be671e25b.png` | failed motion `motion-tiktok-bb7bf0fbdb984e4eaa0f3241b4f253fc.mp4` | known-failed pair |
| C | success reference | failed motion | isolate motion/reference interaction; do not execute |
| D | failed reference | success motion | isolate reference/motion interaction; do not execute |

Before any paid execution, export the exact source bytes/hashes and compare the historical request/operation/media rows. The same provider model, account, mode, ratio, subtype, and prompt must be held constant.

## 15. Recommended Next Action

Collect read-only production evidence for the exact job and render IDs: operation rows, render events, request/submit/poll/error JSON, operation assets, media file metadata, and provider IDs/timestamps. Locate/export the two n8n workflows if they are stored outside this repository. Do not treat provider error text alone as proof of the failing upload stage.

DO NOT PATCH YET.

## 16. Files Changed

Expected and actual audit artifact:

- `RDANCE_79AI_MOTION_AUDIT_REPORT.md`

No production code, Prompt Assistant, RVIDEO, migrations, database rows, media files, or provider tasks were changed.

## 17. Commit / Push

NONE.

## Final Check

Required identifiers present in this report:

- `dance_sell`
- `kling_video_motion_3`
- `49554a92-900a-42ec-8f81-543faa0bfe95`
- `28171031-d86d-4c67-8eff-67aab154df5f`
- `7962f638-8e2a-4ad4-9ce3-3d17ce2f3492`
- `3fe09576-48b5-4e02-ba00-c89ad4a0572f`
- `842d3440df5430b8`
- `56a3fb0578396662`

The report does not primarily discuss RVIDEO, Prompt Assistant, `RenderVideoJobs.razor`, `scene.image_prompt`, or `scene.video_prompt`.
