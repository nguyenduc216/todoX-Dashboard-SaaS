# DanceSell 79AI v2 Auth Contract Fix Report

Date: 2026-08-18

## Root Cause

DanceSell Motion Control uses the 79AI v2 upload, submit, and poll endpoints, but the client still treated parts of the flow like the legacy 79AI form-urlencoded contract. The v2 contract requires `Authorization: Bearer <access_token>` and must not send `access_token` in request bodies.

## Fix

- Media upload requests now send bearer auth and keep multipart body fields limited to `domain`, `project_id`, and the file part.
- Motion Control submit now sends bearer auth and keeps the form body limited to provider motion fields.
- Motion Control poll now uses bearer auth with only `domain` and `project_id` in the body.
- DanceSell poll wiring now opts into the bearer poll mode and uses the route project id.
- DanceSell motion route SQL now uses `/ai/jobs/{task_id}?media=video` for v2 polling.
- Plain-text HTTP 500 submit responses now surface as `http_500` instead of being reported as invalid JSON.

## Motion V2 Request Contract

- Upload image: `POST /ai/upload/image`, bearer auth, multipart `file`, `domain`, `project_id`.
- Upload video: `POST /ai/upload/video`, bearer auth, multipart `video_file`, `domain`, `project_id`.
- Submit: `POST /ai/jobs/video/kling_video_motion_3`, bearer auth, form fields `domain`, `project_id`, `model`, `prompt`, `image_url`, `images[0][url]`, `video_url`, `subType`, `background_source`, `mode`, `ratio`.
- Poll: `POST /ai/jobs/{task_id}?media=video`, bearer auth, form fields `domain`, `project_id`.

## Database

No schema change is required.

Manual route SQL updated for review:

`database/manual/dance-sell-motion/03_switch_79ai_motion_to_upload_url_flow.sql`

SQL must be reviewed/applied before production uses the new v2 poll route.

## Validation

- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore`: passed, 625/625.
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet test TodoX.Dashboard.sln -c Release --no-restore`: passed, 625/625.
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --include TodoX.Web/Services/AiProviders/Ai79TaskClient.cs TodoX.Web/Services/DanceSell/DanceSellRenderHandler.cs TodoX.Web.Tests/Ai79TaskClientTests.cs TodoX.Web.Tests/RDanceFashionDemoPageTests.cs`: passed.
- `git diff --check`: passed with Windows CRLF warnings only.
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`: passed.

Ready to deploy: NO until the manual route SQL is reviewed/applied.
