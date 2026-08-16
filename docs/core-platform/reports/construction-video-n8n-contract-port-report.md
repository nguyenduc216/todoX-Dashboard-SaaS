# Construction Video n8n Contract Port Report

Date: 2026-08-16

## Source Of Truth

Stable workflow audited:

`D:\todoX\79AI\rvideo\todoX-rendervideo-04-video-worker [v52.7.2 max 60s_model clean layout].json`

This is the latest stable RVideo video worker in the `D:\todoX\79AI\rvideo` package. It is newer than the v52.6 stable package and contains the active max-60s video worker contract.

## n8n Submit Contract

Video submit calls:

`POST /create-video`

Form fields:

- `access_token`
- `domain`
- `model`
- `privacy`
- `prompt`
- `translate_to_en=false`
- `project_id`
- `ratio`
- `resolution`
- `duration`
- `mode`
- `images`

Important porting point: the stable n8n worker submits an images JSON descriptor array. It does not submit separate `image` and `image_2` fields.

TodoX C# now builds a two-item `images` JSON descriptor in deterministic order:

1. start image
2. end image

Each descriptor contains:

- `id_base`
- `project_id`
- `url`
- `file_name`

The C# runtime reads `imageInfo.id_base` from stored 79AI image response JSON. It does not fake or synthesize provider ids. If an input image has no deterministic 79AI `id_base`, submit fails locally with a clear configuration/data error before calling 79AI.

## n8n Poll Contract

Video poll calls:

`POST /video`

Form fields:

- `access_token`
- `domain`
- `videoId`

TodoX C# now sends `videoId` for video poll. Image poll still sends `id_base`.

## Status Contract

Success statuses:

- `MEDIA_GENERATION_STATUS_SUCCESSFUL`
- `MEDIA_GENERATION_COMPLETED`
- `SUCCESS`
- `COMPLETED`

Failure statuses:

- `MEDIA_GENERATION_STATUS_FAILED`
- `MEDIA_GENERATION_FAILED`
- `FAILED`
- `FAILURE`
- `ERROR`
- `CANCELLED`
- `CANCELED`
- `REJECTED`

Other pending/unknown statuses remain `RUNNING` instead of being treated as terminal failure.

## Output URL Contract

Video output URL parsing now prioritizes video-specific fields:

- `videoInfo.download_url`
- `videoInfo.downloadUrl`
- `videoInfo.url`
- equivalent `data.videoInfo` and `body.data.videoInfo` shapes

The parser avoids arbitrary recursive URL search for video output so it does not accidentally pick an unrelated image or input URL.

## Prompt Semantics

The C# runtime preserves TodoX Timelapse profile semantics and clip start/end progress in the provider prompt. It sends a server-side prompt only; no customer UI provider/model options were introduced.

## Cancellation And Transient Poll Errors

Cancellation and transient poll errors release the worker claim instead of marking the clip as `FAILED`.

Transient poll failures include HTTP timeout/rate-limit/5xx responses from the provider poll endpoint. Existing submitted provider tasks keep their provider task id and can be polled again by a later worker claim.

Submit failures still persist sanitized request/response diagnostics and fail the current operation according to the existing lifecycle policy.

## Scope Confirmation

No changes were made to:

- DB schema
- provider/model selection
- Core billing
- retry target-aware semantics
- scene mapping
- finalizer behavior
- UI
- production deployment
