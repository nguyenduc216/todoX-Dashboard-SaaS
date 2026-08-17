# DanceSell Motion Control Multipart Fix Report

Date: 2026-08-17

## Root Cause

`79ai / kling_video_motion` is a model-specific Motion Control contract that requires `multipart/form-data` with binary file parts:

- `character_image` as an image file
- `motion_video` as a video file

The previous implementation had already fixed `mode`, `ratio`, and field names, but it still used the generic 79AI form-urlencoded submit path and sent TodoX URLs as strings. 79AI rejected this with a missing-video style error.

## Fix

- Added `IAi79TaskClient.SubmitMultipartAsync(...)`.
- Kept the existing generic `SubmitAsync(...)` form-urlencoded path unchanged for Timelapse and normal 79AI video/image flows.
- DanceSell `kling_video_motion` now submits:
  - `MultipartFormDataContent`
  - string fields: `access_token`, `domain`, `model`, `prompt`, `privacy`, `project_id`, `mode`, `ratio`
  - file fields: `character_image`, `motion_video`
- The provider motion prompt is minimal/empty by default and no longer sends the reference-image virtual try-on prompt to Kling Motion Control.
- Added `IMediaFileService.OpenReadAsync(...)` so local media files can be streamed into multipart parts.
- Media source order:
  - Reference image: `PreparedReferenceMediaId`, then `PreparedReferenceObjectKey`, then `PreparedReferenceUrl`
  - Motion video: `MotionVideoMediaId`, then `MotionVideoObjectKey`, then `MotionVideoUrl`
- Validation before provider submit:
  - image MIME: `image/jpeg`, `image/png`, `image/webp`
  - video MIME: `video/mp4`, `video/webm`
  - video max size: 50 MB when size metadata is available
- Removed retry write to generated column `result_video_url`.
- Removed direct DanceSell repository writes to `result_video_url`; reads remain.
- Polling now treats successful provider status without `download_url` as output-pending and continues polling until the poll window expires with `DANCE_SELL_OUTPUT_URL_TIMEOUT`.
- DanceSell cost estimation now tries synced provider catalog pricing by `provider_code/model/mode/duration` before falling back to route/app config.

## Expected Multipart Fields

```text
access_token = <secret, not logged>
domain = 79ai.net
model = kling_video_motion
prompt = ""
privacy = PRIVATE
project_id = default
mode = standard
ratio = default
character_image = <binary file>
motion_video = <binary file>
```

## Sanitized Request Metadata

```json
{
  "providerCode": "79ai",
  "model": "kling_video_motion",
  "endpointPath": "/create-video",
  "contentType": "multipart/form-data",
  "characterImageField": "character_image",
  "characterImageMime": "image/jpeg|image/png|image/webp",
  "characterImageBytes": 12345,
  "motionVideoField": "motion_video",
  "motionVideoMime": "video/mp4|video/webm",
  "motionVideoBytes": 1234567,
  "motionVideoDuration": null,
  "providerMode": "standard",
  "providerRatio": "default"
}
```

## Validation Notes

- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RDanceFashionDemoPageTests|FullyQualifiedName~DanceSell|FullyQualifiedName~Ai79Task"`: passed, 100/100.
- `dotnet test TodoX.Dashboard.sln -c Release --no-restore`: passed, 621/621.
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --include <changed C# files>`: passed.
- `git diff --check`: passed.
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`: passed.

## Deployment Readiness

Ready to deploy: YES for the DanceSell Motion Control fix after review. The worktree also contains unrelated untracked/modified AiStudio files that are intentionally excluded from this fix commit.
