# DanceSell Motion Control Upload-URL Flow Report

Date: 2026-08-17

## Root Cause

The previous DanceSell fix submitted `character_image` and `motion_video` as binary multipart parts to the generic 79AI `/ai/create-video` flow. Production still rejected the request as missing a video because the verified RDance n8n Motion Control architecture uploads both TodoX assets to 79AI first, then submits their provider-hosted URLs to the Kling Motion Control job endpoint.

## N8N Contract Port

The repository does not contain a serialized RDance n8n workflow file to inspect node-by-node. The implementation ports the authoritative workflow contract supplied for this task:

| n8n field or step | TodoX implementation |
| --- | --- |
| Upload approved reference image | `UploadMediaAsync` to `/ai/upload/image`, multipart field `file` |
| Upload source motion video | `UploadMediaAsync` to `/ai/upload/video`, multipart field `video_file` |
| Uploaded image URL | `providerReferenceImageUrl` in sanitized `request_json` |
| Uploaded video URL | `providerMotionVideoUrl` in sanitized `request_json` |
| Motion submit | `SubmitMotionControlAsync` to `/ai/jobs/video/kling_video_motion_3` |
| `image_url` | uploaded reference image URL |
| `images[0][url]` | same uploaded reference image URL |
| `video_url` | uploaded motion video URL |
| `id_base` | persisted as `provider_task_id` and used for DanceSell poll |

## Motion Request

The final Motion Control submit is `application/x-www-form-urlencoded`:

```text
access_token = <secret, never persisted in logs>
domain = 79ai.net
project_id = default
model = kling_video_motion_3
prompt = ""
image_url = <79AI uploaded reference image URL>
images[0][url] = <same 79AI uploaded reference image URL>
video_url = <79AI uploaded motion video URL>
subType = motion
background_source = input_video
mode = standard
ratio = default
```

No local TodoX URL is submitted to Motion Control, and no binary file is submitted to the generic `/ai/create-video` endpoint.

## Source Selection And Validation

- Reference image source: `PreparedReferenceMediaId`, then `PreparedReferenceObjectKey`, then `PreparedReferenceUrl`.
- Motion video source: `MotionVideoMediaId`, then `MotionVideoObjectKey`, then `MotionVideoUrl`.
- Reference status must be `approved`.
- Image MIME: JPEG, PNG, or WebP.
- Motion MIME: MP4 or WebM.
- Motion size limit: 50 MB when metadata is available.
- Local TodoX media is streamed directly to the 79AI upload endpoints. Public HTTPS download is only a fallback when local storage cannot resolve the media.

## Polling And Retry

- DanceSell uses the submit `id_base` as `provider_task_id`.
- Route config supplies `poll_id_field=id_base`; generic Timelapse video polling retains its existing `videoId` behavior.
- `MEDIA_GENERATION_STATUS_SUCCESSFUL` without `download_url` remains pollable.
- The job completes only after an output URL is returned.
- Retry clears provider task/status/response/poll state and never writes generated column `result_video_url`.

## Route SQL

Run the manual SQL only after reviewing provider catalog pricing:

`database/manual/dance-sell-motion/03_switch_79ai_motion_to_upload_url_flow.sql`

It disables the old `79ai / kling_video_motion` default route, preserves history, and enables exactly one `79ai / kling_video_motion_3` default route with v2 upload, submit, and poll config.

## Validation

Validation run:

- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet test TodoX.Dashboard.sln -c Release --no-restore`: passed, 623/623.
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --include <changed C# files>`: passed.
- `git diff --check`: passed.
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`: passed.

## Deployment Readiness

Ready to deploy: NO until the route SQL has been reviewed and applied, and a live 79AI request confirms the v2 upload response URL shape and Motion Control endpoint contract.
