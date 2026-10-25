# RVideo confirmed pricing snapshot and scene menu icons

FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER
CHANGE_ID: TODOX-20261025-RVIDEO-PRICING-SNAPSHOT-002
Date: 2026-10-25. Agent: Codex.
Baseline: origin/main 89c12d209fe716b336accc9d3c0b72b9646dae8a.
Branch/worktree: feature/TODOX-20261025-RVIDEO-PRICING-SNAPSHOT-002, wt-rvideo-pricing-snapshot. Original workspace and prior user edits remain untouched.

## Verified cause and pricing contract

The old component awaited Prepare/SaveSceneAsync/ReloadAsync and then read its mutable Scene parameter again; the parent reload replaces the scene DTO collection without returning the saved instance. A local regression reproduces a component retaining a 4-second parameter while the saved request is 6 seconds. The explicit saved-request handoff now wins independently of parameter refresh timing, and a persisted/request mismatch refuses the quote. This demonstrates the stale-reference failure mechanism; it does not establish the production rate or prove that the reported 3.2 was a 4-second price.

The old estimate always passed video tier standard; it did not bind the quote, consent and submit to one settings/rate value. PointPricingService is the authoritative customer points rule: active service_point_rate_override for service/video/tier, else tenant point_rate_config. Video unit is per_second by schema contract. SceneVideoRenderHandler resolves the configured tier from capability then provider config, default standard. Customer points = selected seconds * applicable rate, identical to RVideoSceneVideoCompletionService.CalculateActualVideoPoints. There is no model/mode-specific customer point-rate key in this existing service. Catalog provider prices are not customer point prices, so no model surcharge, guessed rate or quality-mode-to-tier mapping is introduced. Different configured tiers/rates produce different totals; models on the same tier share the same canonical rate.

No production SQL/API pricing query was run. Actual production 4/6/8-second totals remain operator-verifiable from the new dialog; 0.8/1.2 rates in tests are fixtures, not evidence of production prices.

## Changes and preserved owners

- RenderVideoJobs returns the fresh saved scene in PrepareRequest, and adds MovieCreation/Refresh/Autorenew/AddCircleOutline/History/Save/EditNote to previously iconless menu items. All 13 menu items have icons; existing handlers/guards/dividers/popover behavior unchanged.
- RVideoSupersedeButton freezes the returned request, consumes the server quote and shows encoded multiline model/mode, selected seconds, full decimal rate/tier and total, preserving risk consent. Uncertain responses retain the same confirmed snapshot, acknowledgement and idempotency key for replay.
- Immutable RVideoSceneVideoOperationSettings captures scene config, selected image/shared source, final prompt, provider routing, model/mode/duration/aspect/resolution and canonical point tier/rate/unit/source/service/total as values/serialized values.
- RVideoSceneVideoRenderAction builds the quote from persisted settings and the canonical route/pricing services, keeps ownership/eligibility checks, compares a fresh snapshot on confirmation and refuses configuration/price drift before draft creation. Canonical draft prepare/activate still own identities, version/generation and fencing.
- SceneVideoOperationDraftService rechecks scene/image selection under its existing tenant/scene/project locks and persists the confirmed settings in the new version config. Parameterized read-only validation queries only; no schema/migration, no old job/version/billing updates.
- SceneVideoWorkerHandler checks current rate before reservation/submission only for new snapshot-bearing operations. A mismatch fails with RVIDEO_QUOTE_CHANGED and requires new consent. Provider-known tasks/old jobs do not get repriced. Existing zero-point pre-submit reservation remains unchanged; its tariff records the confirmed settings/rate and completion charges confirmed candidate seconds * frozen CustomerPointRate.
- Existing initial/legacy EstimateAsync, image atomic enqueue, submit intent/physical-send fences, selected history, polling/recovery and final merge behavior remain unchanged. No real 79AI call, production SQL or IIS deployment.

## Files

TodoX.Web/Components/Pages/RenderVideoJobs.razor
TodoX.Web/Components/Pages/RVideo/RVideoSupersedeButton.razor
TodoX.Web/Services/VideoRender/RVideoSceneVideoRenderAction.cs
TodoX.Web/Services/VideoRender/RVideoSceneVideoOperationSettings.cs
TodoX.Web/Services/VideoRender/SceneVideoOperationDraftService.cs
TodoX.Web/Services/VideoRender/SceneVideoRenderHandler.cs (reuse tier resolver; visibility only)
TodoX.Web/Services/VideoRender/SceneVideoWorkerHandler.cs
TodoX.Web/Tests/SceneVideoSupersedeUiTests.cs
TodoX.Web/Tests/RVideoSupersedePricingSnapshotTests.cs
This record and features/TODOX-RVIDEO-VIDEO-RERENDER.md.

## Verification and delivery

PASS: dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --filter "FullyQualifiedName~SceneVideoSupersedeUiTests|FullyQualifiedName~RVideoSupersedePricingSnapshotTests|FullyQualifiedName~SceneVideoOperationDraftTests" --nologo (48 passed, 0 failed/skipped).
PASS: dotnet build TodoX.Web/TodoX.Web.csproj -c Release --nologo (0 errors; existing warnings).
PASS: git diff --check, UTF-8 validation, Material icon compilation, DI registrations for route/pricing verified.
NOT RUN: full suite, live pricing/provider/IIS/browser acceptance and PostgreSQL integration; operator performs production acceptance. Desktop/mobile layout retains MudMenu's existing popover/spacing; no visual pass claimed.
No new SQL migration required. New snapshot fields use existing JSON columns.

User explicitly requests commit/push origin/main then overwrite the existing D:/todoX/Dashboard-web/TodoXPortal/deployed-todoX-Dashboard-SaaS. Main is unprotected and applicable rules empty (authenticated GitHub API verified). Integration is fast-forward from verified current main; never force push. Stage only these eleven source/test/history files.
Publish preserves appsettings*.json, web.config and uploads via publish-item exclusions; verify config SHA256 and matching final-commit DLL before reporting. No IIS action. Actual remote SHA/publish verification reported by executing agent.
Commit SHA: see Git commit containing this history entry.
