## PROMPT_ID
- `TODOX-RVIDEO-VIDEO-FALLBACK-LIFECYCLE-019-FIX1`

## Repository / Branch
- Repository: `TodoX-Dashboard-SaaS`
- Branch: `integration/rdance-on-construction-video-core`
- Base Commit: `1782dd1c5e7a53247f32869c339c7b1ae71ca79e`

## Final Commit SHA
- Updated after the final commit and push.

## Root Cause And Fix
- `billingScenes` and `imageWorkScenes` had been conflated, so filtering image work could undercount VIDEO seconds and external VOICE.
- `RVIDEO_PARENT_BILLED` had been treated as a project-wide boolean, allowing an older operation to suppress a new operation.
- The customer UI had no shared IMAGE + VIDEO + VOICE estimate before the first paid action.
- The singleton balance notifier carried no customer identity, and voucher success did not reliably notify after commit.
- The 79AI runtime fallback route is capability/catalog driven. The audit confirms `veo_omni/flash` supports `4,6,8,10`, while `veo_3_1` supports `fast,lite,quality`; `normal`, `extremely-crazy`, `extremely-spicy-or-crazy`, and `custom` do not match those models. Invalid candidates are discarded before version creation or provider submit.

## rVideo Billing Scene Scope
- `billingScenes`: all logical video scenes selected for the initial operation.
- `imageWorkScenes`: only scenes that still require AI image work after active-image filtering.
- `image_count`: counted from the AI-generation plan over the image-work subset.
- `video_seconds`: summed from every `billingScenes` scene.
- `voice_count`: counted across every `billingScenes` scene when external voice is enabled.
- Filtering image work can no longer reduce VIDEO or VOICE quantities.

## rVideo Billing Operation Identity
- `billingOperationId`: existing `core_job_id`; parent render job id is the fallback for legacy jobs.
- `parentRenderJobId`: the concrete image-batch or video-batch parent render job id.
- Parent charge reference: `billingOperationId`, used by existing wallet idempotency.
- Snapshot: `render_job_snapshots` stores operation identity, IMAGE/VIDEO/VOICE quantities, all scene durations, rates, points, balance check, and post-charge balance.
- Event metadata: `RVIDEO_PARENT_BILLED` includes `billingOperationId`, `parentRenderJobId`, `projectId`, `serviceId`, `chargeReferenceId`, and usage totals.
- Retry/idempotency: parent-billed checks require matching operation metadata and a charge reference; project-wide event presence is not sufficient. Existing wallet reference idempotency prevents duplicate debit for the same operation.
- Child initial IMAGE, VIDEO, and external VOICE work remains zero-point after a successful parent charge.

## rVideo Customer Cost Preview
- Estimate service: `IRVideoInitialPointEstimateService` / `RVideoInitialPointEstimateService`.
- UI location: `RenderVideoJobs.razor`, immediately below the scene/image toolbar.
- Sufficient state: shows IMAGE, VIDEO seconds, VOICE, total, available points, and remaining points; initial image action stays enabled.
- Insufficient state: shows required, available, and missing points and disables the initial image action.
- Backend revalidation: image-batch and alternate video-batch handlers rebuild the plan, resolve current pricing, read the wallet, and charge immediately before child enqueue.
- Refresh triggers: reload, committed settings/project reload, character changes, aspect-ratio changes, and resolution changes.

## Balance Notification
- Customer-scoped notifier: `IPointBalanceChangeNotifier.Changed` carries `Guid customerId`.
- `CHARGE`: notifies the charged customer once after commit; null-customer and idempotent duplicate paths do not notify.
- `TOPUP`: notifies the affected customer once after commit.
- `ADJUST`: notifies the affected customer once after commit.
- `REFUND`: notifies the affected customer once after commit.
- `VOUCHER`: notifies after wallet mutation, redemption insert, voucher counter update, and transaction commit.
- Cross-customer filtering: `MainLayout.razor` reloads only when the notification customer id matches the current session customer id.

## SQL Verification
- `database/manual/verify_point_module.sql` now includes checks for rVideo operation ids, parent charge event references, duplicate parent charges, voucher redemptions, and wallet/ledger balance comparison.
- No schema change or migration was created, modified, or executed.

## Tests
- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --filter "FullyQualifiedName~UnifiedPointModuleRegressionTests"`: PASS, 7 tests.
- `dotnet test TodoX.Web\TodoX.Web.csproj -c Release --filter "FullyQualifiedName~RVideoProviderPollingRegressionTests|RVideoAutosaveWorkflowTests"`: PASS.
- Full `TodoX.Web.Tests` run: existing unrelated failures remain in dance-sell prompt/ratio and older UI source assertions; task-specific tests pass.
- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --filter "FullyQualifiedName~RVideoVideoHotfixTests|FullyQualifiedName~RVideoProviderPollingRegressionTests"`: pending final validation.

## Build
- `dotnet build TodoX.Dashboard.sln -c Release`: PASS.

## Publish
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`: pending final validation.
- Output: `artifacts/publish/todox-dashboard`

## Git Push
- Pending final commit and push.

## Files Changed
- `TodoX.Web/Components/Layout/MainLayout.razor`
- `TodoX.Web/Components/Pages/RenderVideoJobs.razor`
- `TodoX.Web/Program.cs`
- `TodoX.Web/Services/PointBalanceChangeNotifier.cs`
- `TodoX.Web/Services/Render/SceneImageBatchRenderHandler.cs`
- `TodoX.Web/Services/VideoRender/RVideoInitialPointEstimateService.cs`
- `TodoX.Web/Services/VideoRender/RVideoSceneAudioAutoChainService.cs`
- `TodoX.Web/Services/VideoRender/SceneVideoRenderHandler.cs`
- `TodoX.Web/Services/WalletService.cs`
- `TodoX.Web.Tests/UnifiedPointModuleRegressionTests.cs`
- `TodoX.Web/database/manual/verify_point_module.sql`
- `TodoX.Web/docs/unified-point-module-report.md`

## Remaining Limitations
- The estimate is advisory in the UI; backend wallet and pricing revalidation remains authoritative.
- Existing unrelated full-suite failures were not changed as part of this task.
