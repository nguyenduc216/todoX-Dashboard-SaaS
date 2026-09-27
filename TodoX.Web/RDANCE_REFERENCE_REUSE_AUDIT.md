# RDance Existing 79AI Image Reuse / Summary View Audit

Audit date: 2026-09-23

Scope: read-only source and available forensic-export audit for `dance_sell`, provider 79AI, model `kling_video_motion_3`. No production code, database, media, provider call, retry, upload, submit, commit, or push was performed.

## 1. Executive Summary

- The source does not contain a feature literally named “summary view”. The actual existing-asset path is provider media-list verification through `POST /images`.
- New uploads and previous-attempt reuse are separate branches, but both require provider list verification before motion submit.
- Existing reference reuse is accepted only when the stored asset is marked verified and the live result matches both the stored `idBase` and the stored canonical URL using exact case-insensitive string comparison.
- This URL rule is stricter than the initial list lookup rule. A provider list URL ending in `?full=1` can match the `idBase` during lookup, but still reject reuse because the final URL equality check compares the raw strings.
- Available forensic-export evidence records exactly this behavior: same `idBase` and stored URL without query, live URL with `?full=1`, followed by `AI_PROVIDER_REFERENCE_ASSET_REUSE_REJECTED`.
- The supplied target job IDs are not present in the available local forensic export, so the exact target attempt sequence cannot be proven from local runtime data.

**Classification:** POSSIBLE BUG - NEED RUNTIME EVIDENCE. The source confirms an unnecessary re-upload path can occur after a URL-only representation difference. It does not prove that this caused the supplied target job failure.

## 2. Actual New-Image Flow

```text
approved local reference media
  -> ResolveMotionFileAsync
  -> IAi79TaskClient.UploadMediaAsync
  -> POST /ai/upload/image, multipart field configured by runtime (normally file)
  -> parse provider upload URL and id_base
  -> POST /images with domain and project_id
  -> require matching idBase or upload URL and SUCCESS status
  -> persist provider URL, idBase, uploadUrl, verificationMatched=true, verificationSource=list_images
  -> Kling motion submit
```

Source: `Services/DanceSell/DanceSellRenderHandler.cs`, `Submit79AiAsync`, `VerifyProviderImageAsync`; `Services/AiProviders/Ai79TaskClient.cs`, `UploadMediaAsync` and `ListImagesAsync`.

## 3. Actual Existing-Image Flow

```text
stored verified operation asset
  -> GetLatestAssetForRenderJobAsync
  -> fallback GetLatestAssetAsync for the same dance job/media/object key
  -> IsVerifiedProviderAsset
  -> if from a previous attempt: ReverifyPreviousReferenceAssetAsync
  -> POST /images provider list lookup
  -> require live idBase and URL to equal stored idBase and canonical URL
  -> reuse stored provider URL
  -> otherwise emit AI_PROVIDER_REFERENCE_ASSET_REUSE_REJECTED
  -> fresh local binary upload, verification, persistence, Kling submit
```

The two paths are **PARTIALLY** different: they have distinct reuse/upload branches, but both ultimately use the same list verification contract and the same Kling submit path.

## 4. What “Summary View” Actually Is

No `summary`, `summary_view`, or `summaryView` implementation was found for this integration. The actual terminology is provider media list verification:

| Operation | Method | Endpoint | Form fields | Response fields consumed |
|---|---|---|---|---|
| Existing/new image verification | POST | configured `list_images_path`, default `/images` | `access_token`, `domain`, `project_id` | `id_base`/`idBase`, `url`, `status`/`state`/`provider_status`, `download_url` |
| Motion verification | POST | configured `list_videos_path`, default `/videos` | same | same, with `download_url` accepted for matching |

The client parses list items from `data`, `images`, or `items` for images and `data`, `videos`, or `items` for videos.

## 5. Provider Asset Identity Rules

### Stored asset eligibility

`IsVerifiedProviderAsset` returns true only when:

1. `ProviderUrl` is non-empty HTTPS;
2. metadata has non-empty `idBase`;
3. metadata has `verificationMatched=true`;
4. `verificationSource` is `list_images` or `list_videos`.

Repository lookup additionally restricts by the same `media_id` and/or `object_key`, `provider_url` non-empty, and `verificationMatched=true`, then selects newest `created_at`.

### Live previous-image reuse

`ReverifyPreviousReferenceAssetAsync` creates a synthetic upload result from stored values, then calls `VerifyProviderImageAsync`. The list lookup accepts an item when:

```text
item.IdBase == upload.IdBase (case-insensitive)
OR
item.Url == upload.Url (case-insensitive)
```

The item must normalize to provider `SUCCESS`. After that lookup, reuse is accepted only when:

```text
verified.IdBase == stored idBase (case-insensitive)
AND
verified.Url == stored provider URL (case-insensitive)
```

No filename, hash, local media hash, object-key hash, query-stripping, or URL canonicalization is performed in this decision.

## 6. `?full=1` URL Analysis

The source does not strip query strings or normalize URLs before the final reuse comparison. Therefore:

```text
stored:  .../f22c56ee8f852d48.png
live:    .../f22c56ee8f852d48.png?full=1
```

has the same `idBase`, and passes the initial `OR` list lookup, but fails the later exact URL equality check. This is confirmed by available forensic-export events showing:

```text
idBase       = f22c56ee8f852d48
providerUrl  = .../f22c56ee8f852d48.png
verifiedIdBase = f22c56ee8f852d48
verifiedUrl  = .../f22c56ee8f852d48.png?full=1
event        = AI_PROVIDER_REFERENCE_ASSET_REUSE_REJECTED
```

This is a confirmed source/runtime mismatch mechanism, not proof of provider-side image invalidity.

## 7. `AI_PROVIDER_REFERENCE_ASSET_REUSE_REJECTED` Analysis

There are two code conditions:

| Condition | Location | Meaning | Could apply to target? |
|---|---|---|---|
| Live `idBase` or URL differs from stored value after verification | `ReverifyPreviousReferenceAssetAsync` | Provider list found a successful item, but final identity equality failed | Yes, if target had a prior verified asset and URL representation differed |
| `VerifyProviderImageAsync` throws `Ai79TaskSubmitException` | `ReverifyPreviousReferenceAssetAsync` catch | List request, response, match, or provider status verification failed | Yes, but exact error requires target event data |

The available runtime evidence for the recorded rejection is the first condition, not a missing image: both IDs were identical and only URL representation differed.

## 8. Target Failed Job Reconstruction

For `28171031-d86d-4c67-8eff-67aab154df5f`:

- local reference media ID: `1cdb72e1-247c-45ba-938d-698be671e25b`;
- known provider reference: `19c06fda7204a83d.png`;
- attempt #1 render job: `7962f638-8e2a-4ad4-9ce3-3d17ce2f3492`;
- attempt #2 render job: `3fe09576-48b5-4e02-ba00-c89ad4a0572f`;
- attempt #2 provider task: `56a3fb0578396662`.

The target-specific database rows/events/request JSON are not available in the local workspace/export. Therefore this audit cannot distinguish for the target between URL mismatch, list/status failure, upload result identity behavior, or later Kling failure. The known sequence is consistent with the source branch that rejects reuse and then uploads again, but remains unproven for this job.

## 9. Successful Job Comparison

The supplied successful IDs (`49554a92-900a-42ec-8f81-543faa0bfe95`, render `c95e0823-32e7-4e27-8752-9ac9bf73a607`, provider reference `20488258f158151a.png`) are also absent from the available local runtime export. Its exact new-versus-reused branch cannot be established from available evidence.

The available export does prove that successful operations can use `uploadState=reused_verified`, so reuse is a live production path and is not inherently incompatible with Kling motion submit.

## 10. Attempt #1 vs Attempt #2

| Attempt | Existing provider asset found? | Reverify | Reuse accepted | Fresh upload | Returned idBase | URL used for Kling |
|---|---|---|---|---|---|---|
| #1 `7962f638-8e2a-4ad4-9ce3-3d17ce2f3492` | Not available in local export | Not available | Not available | Not available | Not available | Not available |
| #2 `3fe09576-48b5-4e02-ba00-c89ad4a0572f` | Not available in local export | Not available | Not available | Not available | Not available | Not available |

The prior forensic context identifies attempt #1 as submit-stage upload failure and attempt #2 as a created provider task that later entered error; this report does not infer image-path details that are not present in the available target rows.

## 11. Persistence / Retry Analysis

Provider asset records are stored in `public.todox_ai_operation_assets` with:

- `operation_id`, `asset_role`, `media_id`, `object_key`;
- `public_url`, `provider_url`, `mime_type`;
- JSON metadata including `uploadUrl`, `idBase`, `projectId`, `fileName`, `verificationMatched`, `verificationSource`, status and timestamps.

Render-job lookup is scoped by render job plus media ID/object key. If absent, fallback lookup is scoped by dance job, operation type, asset role, media ID/object key. Cloning a reused asset adds `reusedForRenderJobId` and `reusedForOperationId` while retaining provider identity metadata.

The key persistence risk is not a missing media ID: it is the strict raw URL equality check after live verification. A semantically equivalent provider URL can be rejected and cause fresh upload.

## 12. Can Existing Assets Be Reuploaded Unnecessarily?

**POSSIBLE, with source and runtime evidence.**

When a previous asset is eligible but live list verification returns the same `idBase` with a URL such as `?full=1`, `ReverifyPreviousReferenceAssetAsync` returns false. `Submit79AiAsync` then reaches the fresh `UploadMediaAsync` branch. This is the exact control flow.

The source does not prove that 79AI deduplicates the fresh upload or that a fresh upload must return the same `idBase`. The observed same-identity outcome is therefore **UNKNOWN** as provider behavior: it could be deduplication, stable identity, or an artifact of runtime data.

## 13. Root Cause Relevance

This behavior can explain intermittent differences only as a supported mechanism for changing the request path: reuse versus fresh upload, and possibly the provider URL representation used afterward. It does not by itself prove the image caused the Kling failure.

What is proven:

- reuse can be rejected solely by URL representation despite equal `idBase`;
- rejection leads to fresh upload;
- successful jobs can use verified reused assets;
- the provider list status is required before submit.

What is not proven:

- target job entered this exact `?full=1` branch;
- 79AI returned the same identity because of deduplication;
- fresh upload versus reuse caused provider task `56a3fb0578396662` to fail.

## 14. Exact Evidence Still Missing

1. Target `dance_sell_provider_operations` rows for both render jobs.
2. Target `todox_ai_operation_assets` rows before and after each attempt.
3. Target render event rows, especially `AI_PROVIDER_REFERENCE_ASSET_REUSE_REJECTED`, including `verifiedUrl`, `verifiedIdBase`, and `errorCode`.
4. Target `request_json` and `submit_response_json` for both attempts.
5. Target provider account/project/domain and exact image list response.
6. Target successful job rows for direct path comparison.

## 15. Recommended Next Diagnostic

Export the target rows and event JSON read-only, then compare `providerUrl`, `uploadUrl`, `idBase`, `verifiedUrl`, `verificationSource`, `uploadState`, and the exact submit `image_url`/`images[0][url]`. This will distinguish URL-only rejection from provider status failure and from post-submit Kling failure.

## 16. Files Changed

- `RDANCE_REFERENCE_REUSE_AUDIT.md` (new report only)

## 17. Commit / Push

None. No commit or push was performed.
