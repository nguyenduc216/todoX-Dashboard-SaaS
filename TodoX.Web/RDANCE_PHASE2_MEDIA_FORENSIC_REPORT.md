# RDance Phase 2 Media Forensic Report

Audit date: 2026-09-23

Scope: read-only forensic follow-up for RDance / `dance_sell`, provider 79AI, model `kling_video_motion_3`. No production code, database data, media, provider task, retry, upload, submit, migration, commit, or push was performed.

## 1. Executive Summary

- All four original media files: **NOT AVAILABLE** in the local workspace and configured local storage roots.
- Media validity and technical differences: **UNKNOWN** because the exact bytes were unavailable.
- Image evidence: **UNKNOWN**. PNG is not implicated by extension; the success and failed references are both described as PNG.
- Motion evidence: **UNKNOWN**. The failed MP4 could not be inspected.
- Provider readiness evidence: **UNKNOWN** for the target jobs because their production event timestamps were not available.
- Root cause: **not proven**.

Phase 1 source conclusions remain valid: Dashboard RDance uses a separate `dance_sell` path; `request_json` is not the exact wire payload; the two failed attempts represent different failure stages; and the stale provider-status diagnostic issue exists in source.

Required conclusion:

**ROOT CAUSE NOT YET PROVEN.**

## 2. Evidence Sources

### Source and configuration

- `Services/DanceSell/DanceSellRenderHandler.cs`
- `Services/DanceSell/DanceSellPhase2Services.cs`
- `Services/DanceSell/DanceSellRepository.cs`
- `Services/DanceSell/DanceSellAiOperations.cs`
- `Services/DanceSell/DanceSellCompletionService.cs`
- `Services/AiProviders/Ai79TaskClient.cs`
- `Services/Media/MediaFileService.cs`
- `Services/Media/LocalMediaPathResolver.cs`
- `appsettings.json`
- `RDANCE_79AI_MOTION_AUDIT_REPORT.md`

### Database evidence

The repository's existing .NET collector was inspected and run in its existing read-only mode against `TodoXSaaS`. It queried `information_schema` and `SELECT` statements only, but its configured target IDs belong to an earlier investigation, not this Phase 2 task. It contains no rows for:

- `49554a92-900a-42ec-8f81-543faa0bfe95`
- `28171031-d86d-4c67-8eff-67aab154df5f`
- `c95e0823-32e7-4e27-8752-9ac9bf73a607`
- `7962f638-8e2a-4ad4-9ce3-3d17ce2f3492`
- `3fe09576-48b5-4e02-ba00-c89ad4a0572f`

No direct target-specific production export was available. No database writes were executed.

### Local media search

Read-only searches covered:

- `wwwroot/uploads`
- `wwwroot/uploads/dance-sell`
- `D:\TodoXData\shared-media` when present
- the broader `D:\todoX` workspace

The four exact filenames were not found. No substitute media was inspected.

### Tools

- PowerShell filesystem search and file metadata inspection
- repository source search with `rg`
- existing .NET read-only database collector

`ffprobe`, `ffmpeg`, ImageMagick `identify`, `magick`, and `file` were not available in PATH, and the target media bytes were not available regardless.

## 3. Asset Resolution

| Asset | Media ID | Expected object key / filename | Physical path | Exists | Bytes | SHA256 |
|---|---|---|---|---|---:|---|
| SUCCESS_REFERENCE | `c5c2fcb8-68a1-42e0-87b3-48bff58a178d` | `c5c2fcb868a142e087b348bff58a178d.png` | NOT AVAILABLE | No | N/A | N/A |
| FAILED_REFERENCE | `1cdb72e1-247c-45ba-938d-698be671e25b` | `1cdb72e1247c45ba938d698be671e25b.png` | NOT AVAILABLE | No | N/A | N/A |
| SUCCESS_MOTION | `a5e09c5d-c1fe-4934-a540-798308aae672` | `motion-tiktok-8c8036a6889e448fb73741c5bfa05696.mp4` | NOT AVAILABLE | No | N/A | N/A |
| FAILED_MOTION | `2b4bf3d0-757e-436e-a32e-8c2785448f32` | `dance-sell/2610a0bf33084c339df16eaad2585b7c/202609/motion-tiktok-bb7bf0fbdb984e4eaa0f3241b4f253fc.mp4` | NOT AVAILABLE | No | N/A | N/A |

Storage configuration confirms the normal local root is `wwwroot/uploads`; the application also has `D:\TodoXData\shared-media` configured for shared media. Neither contained the required exact files. The success object key could not be resolved from the target database row.

## 4. Reference Image Comparison

The requested files are:

- SUCCESS: `c5c2fcb868a142e087b348bff58a178d.png`
- FAILED: `1cdb72e1247c45ba938d698be671e25b.png`

Both are **NOT AVAILABLE** locally. Therefore the following are UNKNOWN for both files and cannot be compared:

- detected binary format and signature;
- MIME, dimensions, aspect ratio, channels, alpha usage, bit depth, color space;
- ICC, gamma, chromaticity, orientation, EXIF, embedded metadata;
- interlace/compression details;
- decode validity, truncation, and corruption indicators;
- SHA-256 or MD5.

The `.png` extension agrees with the supplied naming convention only; actual binary format was not tested. PNG extension is not evidence of causality because the success reference is also PNG.

Classification: UNKNOWN.

## 5. Motion Video Comparison

The requested files are:

- SUCCESS: `motion-tiktok-8c8036a6889e448fb73741c5bfa05696.mp4`
- FAILED: `motion-tiktok-bb7bf0fbdb984e4eaa0f3241b4f253fc.mp4`

Both are **NOT AVAILABLE** locally. The following are UNKNOWN:

- container, duration, start time, size, bitrate, probe score, brands, and tags;
- codec, profile, level, dimensions, pixel format, color metadata, frame rates, time base, timestamps, frame count, rotation, and display matrix;
- audio presence and audio codec parameters;
- CFR/VFR status, timestamp gaps, duplicate/non-monotonic timestamps;
- decode errors, invalid NAL units, corrupt packets, missing frames, and audio errors;
- SHA-256 or MD5.

No provider media was downloaded. No media was re-encoded or modified.

## 6. Decode Validation

No decode validation was possible because the exact four original files were unavailable. No `ffmpeg` or equivalent decoder was run against substitute files.

Result: **NOT AVAILABLE**, not “NO DECODE ERRORS.”

## 7. Frame Timing Comparison

SUCCESS motion: UNKNOWN.

FAILED motion: UNKNOWN.

No frame timestamps, `r_frame_rate`, `avg_frame_rate`, duration, or frame count could be read. CFR/VFR classification is UNKNOWN for both.

## 8. Provider Upload / Verify Timing

The exact target `render_job_events`, operation rows, and provider timestamps were unavailable. Therefore no measured timing table can be produced.

| Attempt | Motion upload completed | Motion verify completed | Submit started | Verify-to-submit delay | Submit duration | Result |
|---|---|---|---|---|---|---|
| SUCCESS `c95e0823-32e7-4e27-8752-9ac9bf73a607` | UNKNOWN | UNKNOWN | UNKNOWN | UNKNOWN | UNKNOWN | Task-supplied SUCCESS |
| FAILED #1 `7962f638-8e2a-4ad4-9ce3-3d17ce2f3492` | UNKNOWN exact timestamp | UNKNOWN exact timestamp | UNKNOWN exact timestamp | UNKNOWN | UNKNOWN | Task-supplied submit failure |
| FAILED #2 `3fe09576-48b5-4e02-ba00-c89ad4a0572f` | UNKNOWN exact timestamp | UNKNOWN exact timestamp | UNKNOWN exact timestamp | UNKNOWN | UNKNOWN | Task created, later ERROR |

The source confirms that upload verification precedes submit, but source order alone cannot establish a readiness race. No fixed-delay recommendation is justified.

## 9. Historical Provider Request Comparison

Source-confirmed effective RDance wire contract:

```text
POST {baseUrl}{motionSubmitPath}
Authorization: Bearer <redacted>
Content-Type: application/x-www-form-urlencoded

domain=<domain>
project_id=<project_id>
model=<model>
prompt=<prompt>
image_url=<reference provider URL>
video_url=<motion provider URL>
subType=<subType>
background_source=<background_source>
mode=<mode>
ratio=<ratio>
images[0][url]=<reference provider URL> when enabled
```

The current Dashboard handler constructs the URL-based request without multipart file parts. The supplied task context reports broadly common values: model `kling_video_motion_3`, mode `standard`, ratio `default`, `subType=motion`, and an empty historical prompt.

Exact historical values for the target rows are UNKNOWN because the target request/operation exports were unavailable. Do not treat internal `request_json` as the exact wire body.

## 10. Attempt Reconstruction

### Success: `c95e0823-32e7-4e27-8752-9ac9bf73a607`

Task-supplied flow:

```text
reference upload -> reference verify -> motion upload -> motion verify
-> submit kling_video_motion_3 -> task 842d3440df5430b8
-> RUNNING -> SUCCESS
```

Exact database timestamps, provider response JSON, provider account, URLs, and asset metadata: UNKNOWN.

### Failed attempt 1: `7962f638-8e2a-4ad4-9ce3-3d17ce2f3492`

Task-supplied result: submit-stage failure after media upload/verification. No provider task was supplied. This must not be merged with attempt 2.

Exact request/response/event rows: UNKNOWN.

### Failed attempt 2: `3fe09576-48b5-4e02-ba00-c89ad4a0572f`

Task-supplied flow:

```text
reference upload/verify OK -> motion upload/verify OK
-> submit success -> task 56a3fb0578396662
-> RUNNING -> RUNNING -> ERROR
```

This is a provider task/poll failure, not a local upload failure. Exact poll response and timestamps: UNKNOWN.

## 11. Hypothesis Evaluation

### H1 - Reference image incompatibility

Evidence for: none available from bytes or decode metadata.

Evidence against: success and failed references are both reported as PNG; extension alone is irrelevant.

Classification: UNKNOWN, currently low evidence.

### H2 - Motion video incompatibility

Evidence for: the failed motion asset is a distinct file/provider asset and attempt 2 reached provider task creation before ERROR.

Evidence against: no codec, profile, timestamp, decode, or container metadata is available.

Classification: HIGH CONFIDENCE hypothesis from job specificity only; not proven technically.

### H3 - Provider media readiness race

Evidence for: source verifies list visibility/status before submit, but list verification does not prove inference readiness.

Evidence against: no target upload/verify/submit timestamps. It is unknown whether success and failed attempts submitted immediately with equivalent timing.

Classification: HIGH CONFIDENCE hypothesis from architecture; UNKNOWN for target evidence.

### H4 - Provider transient/internal failure

Evidence for: attempt 2 created task `56a3fb0578396662` and later returned ERROR; this is compatible with provider-side processing failure.

Evidence against: no unsanitized/complete terminal provider response is available.

Classification: HIGH CONFIDENCE hypothesis, not proven.

### H5 - Application payload difference

Evidence for: historical prompt mapping and internal-vs-wire distinction are known risk areas; target historical wire payload is unavailable.

Evidence against: supplied context says success and failed settings were broadly the same.

Classification: UNKNOWN.

### H6 - Retry/asset reuse state

Evidence for: source creates a new operation/render attempt and can reuse prior provider assets after live verification; reuse mismatch triggers fresh upload.

Evidence against: target operation asset rows and event metadata are unavailable.

Classification: HIGH CONFIDENCE application mechanism, UNKNOWN as the cause here.

## 12. Root Cause Assessment

The required media bytes, target production rows, exact provider request/response values, and timing evidence were not available in this workspace. No image or MP4 technical abnormality can be established, and no timing comparison can support or reject a readiness race for these exact jobs.

**ROOT CAUSE NOT YET PROVEN.**

## 13. Is A/B C/D Test Still Required?

**YES**, if production evidence and original media become available and root cause remains unresolved.

- A: success reference + success motion, known-success control.
- B: failed reference + failed motion, known-failed control.
- C: success reference + failed motion, isolates motion/reference interaction.
- D: failed reference + success motion, isolates reference/motion interaction.

C/D were not executed. No provider request or paid task was created.

## 14. Recommended Next Step

Export read-only production rows for the exact IDs, including `dance_sell_jobs`, provider operations, operation assets, `render_jobs`, and `render_job_events`, and expose the original local media/object keys from the media store. Then hash and probe only those exact files. Do not patch, transcode, retry, or run C/D until that evidence is available.

## 15. Files Changed

- `RDANCE_PHASE2_MEDIA_FORENSIC_REPORT.md`

No production code, Prompt Assistant, RVIDEO, database schema/data, media, or prior Phase 1 report was changed.

## 16. Commit / Push

NONE.

## Final Safety Check

- No production code changed: confirmed.
- No DB writes: confirmed; only a read-only collector was run.
- No provider submit: confirmed.
- No provider media upload: confirmed.
- No retry or paid task: confirmed.
- No media transcoding: confirmed.
- No production media modified: confirmed.
- No commit: confirmed.
- No push: confirmed.
- Report only: confirmed, aside from reverting the collector's generated evidence file to avoid leaving an audit artefact change.
