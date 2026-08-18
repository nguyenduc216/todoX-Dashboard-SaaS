# DanceSell / RDance Production Fix Report

## Scope

Branch: `integration/rdance-on-construction-video-core`

This change completes the current DanceSell/RDance production hardening and adds the customer-selectable `autoFinish` mode for RDance, Timelapse, and RVideo. No deployment, merge, migration, or schema change was performed.

## Root cause

The no-product RDance path could treat the dashboard-hosted prepared reference URL as sufficient for motion submission. That URL is not proof that the image exists on the motion provider. The render handler now always resolves the approved reference binary and ensures a provider-side image upload exists before motion submit.

## Reference and motion pipeline

Before:

`prepared reference URL -> sometimes direct motion submit`

After:

`character + optional product -> prepared reference version -> explicit approval -> resolve reference binary -> reuse or upload provider reference -> reuse or upload provider motion video -> submit only provider URLs -> poll -> persist result`

No-product jobs use the character image as the prepared reference and do not invoke product try-on generation.

## Idempotency and diagnostics

Provider reference uploads use the `motion_reference_provider_upload` asset role and are keyed by the current prepared-reference media/object identity. Source motion uploads continue to use the existing persisted asset identity. Retries therefore reuse both provider URLs when the source identity is unchanged.

Recorded reference events include:

- `AI_PROVIDER_REFERENCE_UPLOAD_STARTED`
- `AI_PROVIDER_REFERENCE_UPLOAD_COMPLETED`
- `AI_PROVIDER_REFERENCE_UPLOAD_REUSED`

Motion submit diagnostics retain sanitized request/response data and include provider-side reference and motion URLs without access tokens.

## Customer and admin UI

Customer-facing RDance pages no longer show provider/model/raw error details. Customer errors use the centralized safe policy. Technical provider, model, task, and raw error details remain available inside an admin-only section.

The Video tab now contains three responsive business cards: motion preview, approved reference preview with owned download endpoint, and render summary. At desktop widths each card is an equal `lg=4` column with `min-width: 0`, `box-sizing: border-box`, and media constrained to `max-width: 100%`; the cards stack responsively on smaller screens. The previous custom 36.36%/27.28% flex sizing that could wrap the third card was removed.

The approved image endpoint verifies job ownership, approval state, public HTTPS safety, and returns an attachment filename such as `todox-anh-tham-chieu-{jobId}.jpg`.

## Auto Finish

`autoFinish` is persisted in existing request/options JSON, so no database migration is required.

- RDance: after required inputs are ready, prepared reference approval and render queueing continue automatically; manual mode remains available.
- Timelapse: confirmation is bypassed only when `autoFinish=true`; the existing workflow/worker engine remains unchanged.
- RVideo: the existing scene/video/merge engine is advanced by an idempotent polling continuation guard; manual mode remains available.

For new jobs, both `DanceSellDraftCreateRequest` and `DanceSellCreateJobRequest` default `AutoFinish=true`, while historical `DanceSellJobDto` values remain backward-compatible. The RDance create page exposes the switch as ON with explicit ON/OFF helper text. The detail page persists changes only while the job is editable, shows the current state in the output summary, and bypasses the manual confirmation dialog when Auto Finish is enabled. Timelapse create also defaults ON, displays the full workflow helper text, and its detail edit switch is disabled after the draft is no longer editable. Existing manual mode remains available.

## Current UI task scope

This UI refinement changed only RDance and Timelapse presentation/default wiring. No RVideo files were changed for this task. The RDance Information tab is now an outer `md=8`/`md=4` layout: project information and TikTok/MP4 source are separate cards inside the left column, with output summary on the right. The Auto Finish setting is inside the project card and the right-side summary reports `Bật` or `Tắt`.

Existing cancel, retry, billing, result persistence, provider authentication, polling parser, and Timelapse behavior remain intact.

## Changed files

- `TodoX.Web.Tests/RDanceFashionDemoPageTests.cs`
- `TodoX.Web.Tests/TimelapsePhase2CTests.cs`
- `TodoX.Web/Components/Pages/RDanceJobCreate.razor`
- `TodoX.Web/Components/Pages/RDanceJobDetail.razor`
- `TodoX.Web/Components/Pages/RenderVideoJobs.razor`
- `TodoX.Web/Components/Pages/TimelapseJobCreate.razor`
- `TodoX.Web/Components/Pages/TimelapseJobCreate.razor.css`
- `TodoX.Web/Components/Pages/TimelapseJobDetail.razor`
- `TodoX.Web/Models/Timelapse/TimelapseModels.cs`
- `TodoX.Web/Services/DanceSell/DanceSellModels.cs`
- `TodoX.Web/Services/DanceSell/DanceSellPhase2Endpoints.cs`
- `TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs`
- `TodoX.Web/Services/DanceSell/DanceSellRenderHandler.cs`
- `TodoX.Web/Services/DanceSell/DanceSellRepository.cs`
- `TodoX.Web/Services/Timelapse/TimelapseJobService.cs`
- `TodoX.Web/Services/Timelapse/TimelapseWorkflowService.cs`
- `TodoX.Web/docs/dance-sell-rdance-autofinish-production-report.md`

## Validation

- `dotnet test TodoX.Dashboard.sln -c Release --no-restore`: **644 passed**
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: **passed, 0 warnings, 0 errors**
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --include TodoX.Web/Services/DanceSell/DanceSellModels.cs TodoX.Web.Tests/RDanceFashionDemoPageTests.cs TodoX.Web.Tests/TimelapsePhase2CTests.cs`: **passed**
- `git diff --check`: **passed**
- Publish command: `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`
- Publish target: `artifacts/publish/todox-dashboard`
- Deployment: **not performed**

Final commit SHA: recorded in the repository commit metadata for this report.

READY TO DEPLOY: **NO** until the reviewed commit is deployed and a live customer smoke test confirms no-product and product RDance render paths.
