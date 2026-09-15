# DanceSell Motion Video Payload Fix Report

Date: 2026-08-17

## Root Cause

The DanceSell 79AI motion-video submit flow was sending business/UI values directly to Kling Motion Control:

- `mode = 720p`
- `ratio = 9:16`
- image/video fields from code defaults instead of the provider route contract

Production route config requires:

- `mode = standard`
- `ratio = default`
- `reference_image_field = character_image`
- `motion_video_field = motion_video`

## Fix

- Added a central `DanceSellMotionProviderContract` adapter.
- Resolved provider `mode`, `ratio`, and field names from `dance_sell/motion_video` route `config_json`.
- Kept business mode mapping only as fallback:
  - `720p -> standard`
  - `1080p -> professional`
- Required an approved prepared reference before provider submit.
- Sent `PreparedReferenceUrl` as the main reference image.
- Cleared stale provider task, submit/poll/callback, error, and result state on retry.
- Reused the existing RDance processing overlay in the result tab for queued/submitted/rendering states.
- Updated the manual RDance motion-route SQL seed to match the production route config.

## Expected 79AI Form Payload

```text
access_token = <secret, not logged>
domain = 79ai.net
model = kling_video_motion
prompt = <job prompt>
type = video
character_image = <approved PreparedReferenceUrl>
motion_video = <staged TikTok or uploaded MP4 URL>
mode = standard
ratio = default
```

The request no longer sends:

```text
mode = 720p
ratio = 9:16
```

## Cost Estimate

DanceSell cost estimation now uses the provider mode resolved from route config/fallback mapping, so Kling pricing lookup uses `79ai/kling_video_motion/standard` instead of the business quality value `720p`.

## Validation

- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore`
  - Passed: 617
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --include <changed C# files>`
  - Passed
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`
  - Passed: 0 warnings, 0 errors
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`
  - Passed
- `git diff --check`
  - Passed

## Deployment Readiness

Ready to deploy: YES, after the reviewed branch is deployed with the matching `dance_sell/motion_video` provider route config.
