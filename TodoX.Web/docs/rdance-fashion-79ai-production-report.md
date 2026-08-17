# rDance Fashion 79AI Production Report

## Git

- Starting commit: `e5911e377120d5b100a4a80ff364c1d6e3e28fc3`
- Branch: `main`
- Final commit: pending at report creation time
- Push: pending at report creation time

## Changed Files

- `TodoX.Web/Components/Pages/RDanceFashionDemo.razor`
- `TodoX.Web/Services/DanceSell/DanceSellModels.cs`
- `TodoX.Web/Services/DanceSell/DanceSellAiOperations.cs`
- `TodoX.Web/Services/DanceSell/DanceSellPhase1Endpoints.cs`
- `TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs`
- `TodoX.Web/Services/DanceSell/DanceSellRenderHandler.cs`
- `TodoX.Web.Tests/RDanceFashionDemoPageTests.cs`
- `TodoX.Web.Tests/TimelapsePhase2BTests.cs`
- `database/manual/rdance-fashion/01_seed_79ai_kling_motion_routes.sql`

## Database

- Created additive/manual SQL only: `database/manual/rdance-fashion/01_seed_79ai_kling_motion_routes.sql`
- Schema changes: none
- Seed changes: route seed for DanceSell/rDance Fashion provider routing
- SQL executed: no

## Provider Configuration

- Motion primary provider/model: `79ai / kling_video_motion`
- Business-facing model name: `Kling Motion Control`
- Motion backup provider/model: `kie / kling-2.6/motion-control`
- Reference primary provider/model: `79ai / seedream_5_0`
- Reference route output: real AI-generated fashion reference image, not a side-by-side local composite
- Reference backup provider/model: existing KIE route may remain available but is not the primary default

The repository uses the existing 79AI `/generateImage` contract shared with the Timelapse image runtime for the fashion reference step. The manual route seed disables the old `local_composite` default for `dance_sell.reference_image` and enables `79ai / seedream_5_0` with `base64Image` for the character edit base and `subjects` for the product reference, plus `editImage=true`, `project_id=default`, `ratio=9:16`, `mode=vip`, and `resolution=2k`. No `image_2` field is sent for image generation.

The repository has no non-empty `subjects` example or object-item schema. The DanceSell adapter uses a JSON-stringified array of product image data URI strings and records that provisional transport explicitly. The unexecuted manual switch script `database/manual/rdance-fashion/02_switch_reference_route_to_gpt_image_2.sql` prepares `79ai / imagegen_2_0` with `mode=medium`, `resolution=2k`, and `ratio=9:16` after live payload verification.

## Workflow

`TikTok URL or MP4 upload -> server-side staging/media -> character image -> optional product image -> generated or character-backed reference -> explicit approval -> render confirmation -> queued render job -> 79AI Kling Motion Control submit/poll -> result video`

The `/rdance-fashion-demo` page is no longer mock-driven. It uses the existing DanceSell services for draft creation, TikTok staging, media upload, reference creation/approval, render queueing, polling, cancellation and retry.

## Billing

The page displays estimated TodoX points via the existing `IDanceSellCostEstimator` and current wallet balance when available. It does not create a separate billing system. Actual reserve/charge/refund behavior remains governed by the existing DanceSell/render-job billing architecture and feature flags.

## Stop / Retry

- Stop: calls the existing render job cancel mechanism through `IDanceSellPhase2Service.CancelAsync`.
- Retry: reuses the existing DanceSell retry path and existing approved reference/video assets.
- Rerender does not force reference regeneration.
- Reference input changes invalidate the previous selected reference and auto-trigger a new AI reference generation attempt.

## Validation

- `dotnet restore`: passed
- `dotnet build TodoX.Dashboard.sln -c Release`: passed
- `dotnet test TodoX.Dashboard.sln -c Release --no-restore`: passed, 452 passed, 0 failed, 0 skipped
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --include <changed C# files>`: passed
- `git diff --check`: passed
- `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`: passed

Publish output:

- `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

## Remaining Gaps

- The 79AI motion submit path uses the existing generic 79AI task client and route-configurable field names. A live provider render should be smoke-tested after the manual route seed is reviewed/applied.
- SQL seed was generated only and not executed.
- Before live retry, manually apply/review `database/manual/rdance-fashion/01_seed_79ai_kling_motion_routes.sql` so production no longer defaults `dance_sell.reference_image` to `local_composite`.
