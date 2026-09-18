# RDance Motion 2100 + First Submit + Result Action Fix

Date: 2026-09-18
Baseline: `42f606538a1e6b807adb965f08efafeedc90d07e`

## Scope

The change is limited to RDance/Dance Sell queue idempotency and the 79AI response parser. The provider remains `79ai`; the model remains `kling_video_motion_3`. No provider endpoint, payload field, database schema, migration, production service, or production job was changed.

## Root Cause: 2100

Production evidence shows that the affected render uploaded and list-verified both source assets, then Kling Motion submit returned a response containing numeric `error: "2100"` and the message `Tải media lên thất bại, vui lòng kiểm tra file và thử lại.` No provider task ID was created. The existing parser searched `error_code`, `errorCode`, and `code`, but not a numeric value in the `error` field. The failure was therefore persisted as generic `provider_error` even though the raw response retained `2100`.

The parser now preserves `2100` as `Ai79TaskSubmitException.ErrorCode` only for this known provider response shape. The raw sanitized response and provider message remain unchanged. The render handler continues to classify provider submit rejection as permanent, so this does not create an unbounded retry loop or hide an input rejection behind retries.

There was no accessible copy of the production motion files in the repository or local upload tree. Consequently, ffprobe comparison of codec, FPS, bitrate, duration, and pixel format could not be performed. The report does not claim that file size alone explains 2100, and the provider has not supplied an authoritative semantic definition for the numeric code.

## Root Cause: First Submit

The first production attempt failed earlier than Motion submit because the reference multipart stream could not be opened (`missing_file`). The existing durable-media resolver already attempts local storage first and HTTPS public-URL recovery second. The first-submit lifecycle also had a race: `QueueRenderAsync` checked the job status, then charged, created the provider operation, enqueued the render, and updated the Dance Sell job without a per-job gate. Concurrent first clicks could both pass the initial status check.

`QueueRenderAsync` now uses a per-job semaphore around the complete check/charge/operation/enqueue/update sequence. This preserves the existing queue endpoint and causes the second concurrent request to observe the active state and return `DANCE_SELL_JOB_ALREADY_ACTIVE`. No automatic provider retry was added.

## Result Action, Loading, Polling

The existing `RDanceJobDetail` empty result state already renders the current reference image, prompt, motion readiness, centered action, and calls the existing `ConfirmAndQueueAsync` workflow. Existing regression tests also confirm that it does not call a second queue endpoint.

Image upload, MP4 upload, TikTok resolving, and polling states were already present and covered. Polling reloads the job every three seconds while active, survives transient refresh failures, and stops only after a successfully loaded terminal state. These UI paths were left unchanged.

## Validation

Targeted command:

`dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj --no-restore --filter 'FullyQualifiedName~Ai79TaskClientTests|FullyQualifiedName~RDanceVideoFlowRegressionTests|FullyQualifiedName~DanceSellRenderHandlerTests'`

Result: 54 passed, 2 failed. The two failures are pre-existing legacy `create-video` task-id parsing tests (`VideoSubmit_KeepsLegacyAsyncTaskAliases` and `VideoSubmit_UsesVerifiedCreateVideoContractWithExplicitStartAndEndFields`); the new 2100 regression and RDance flow tests pass.

Build command:

`dotnet build TodoX.Dashboard.sln -c Release --no-restore -p:UseSharedCompilation=false`

Result: succeeded, 0 errors. Existing generated nullable warnings and one `FormatterServices` warning remain.

Publish command:

`dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -p:UseSharedCompilation=false -o artifacts\publish\todox-dashboard`

Result: succeeded. Output directory: `artifacts\publish\todox-dashboard`. Existing generated nullable warnings remain; no publish errors occurred.

## Files Changed

- `TodoX.Web/Services/AiProviders/Ai79TaskClient.cs`
- `TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs`
- `TodoX.Web.Tests/Ai79TaskClientTests.cs`
- `TodoX.Web/Tests/RDanceVideoFlowRegressionTests.cs`

## Explicitly Untouched

Image generation, unrelated video generation, voice/audio, points/billing rules, provider routing/model selection, migrations, production database, IIS deployment, and production job `5ec35e57-4958-4991-b833-2e8c05e9debb` were untouched.
