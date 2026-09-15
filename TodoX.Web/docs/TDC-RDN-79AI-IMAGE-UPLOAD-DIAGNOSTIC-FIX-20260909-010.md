# TDC-RDN-79AI-IMAGE-UPLOAD-DIAGNOSTIC-FIX-20260909-010

Date: 2026-09-09
Branch: `feature/rdn-onepage-ui-revamp`
Regression job: `450355fe-2d0b-443a-8f5a-3b271c8fe0f8`

## Root Cause

The RDance 79AI Motion Control submit request omitted the required `model`
form field even though `Ai79MotionControlSubmitRequest` already carried the
resolved model. The provider route and repository contract require
`model=kling_video_motion_3`.

The regression test had also locked the incorrect behavior by asserting that
the submit body did not contain `model`.

## Actual Image #1

The persisted/rendered provider flow used the approved reference image:

`https://ai-cdn.gommo.net/ai/images/9c8318abfa3d29c4/827ac4462157b1c2.jpg`

The image upload completed successfully and provider media verification
confirmed the provider-hosted URL. The failure was not caused by a TodoX
localhost URL, an internal URL, or an unavailable TodoX media record.

## Actual Provider Request

Endpoint:

`POST /ai/jobs/video/kling_video_motion_3`

Before this fix, the form body contained:

- `domain`
- `project_id`
- `prompt`
- `image_url`
- `images[0][url]`
- `video_url`
- `subType`
- `background_source`
- `mode`
- `ratio`

It did not contain `model`.

The minimal fix now adds:

`model=kling_video_motion_3`

Bearer authentication, provider-uploaded media URLs, endpoint, mode, ratio,
and all other workflow behavior remain unchanged.

## Provider Response

The provider accepted task creation and returned task id
`f080501d1976b316`. Polling later returned:

`status=ERROR`

`message=Lỗi upload ảnh #1`

The provider response included the submitted image URL in
`videoInfo.images[0].url`. Credentials are excluded from this report.

The live task was not re-enqueued after the fix, so post-deployment provider
success is not claimed here.

## Changed Files

- `Services/AiProviders/Ai79TaskClient.cs`
  - Include the resolved `model` in the 79AI Motion Control form payload.
- `TodoX.Web.Tests/Ai79TaskClientTests.cs`
  - Regression assertion now requires `model=kling_video_motion_3`.
- `docs/TDC-RDN-79AI-IMAGE-UPLOAD-DIAGNOSTIC-FIX-20260909-010.md`
  - Diagnostic and implementation report.

## Why This Is the Minimal Fix

The provider upload, media verification, reference selection, motion upload,
render queue, polling, billing, points, database schema, UI, KIE integration,
and reference state machine were not changed. The fix restores one missing
field at the existing provider submit boundary and adds the matching focused
test.

## Tests

- `dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Ai79TaskClient"`
  - PASS: 48 tests
- `git diff --check`
  - PASS

## Build

- `dotnet build TodoX.Web.csproj -c Release --no-restore`
  - PASS: 0 errors
  - Existing generated Razor nullable warnings remain.

## Publish

- `dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`
  - PASS
- Output: `artifacts/publish/todox-dashboard`

## Git

Commit message:

`TDC-RDN-79AI-IMAGE-UPLOAD-DIAGNOSTIC-FIX-20260909-010`

