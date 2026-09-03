## Repository / Branch

- Repository: `TodoX-Dashboard-SaaS`
- Branch: `integration/rdance-on-construction-video-core`

## Point Module Base Commit

- `6bac4563f708954510e135f745be1d78e024d441`

## Point Module Final Implementation Commit

- `39197e12eb64579f16f8b270e7d154d3097475e1`

## Final Branch HEAD

- `202fe444460b14cb34b6be5df1e4f66e91f9871f`

## Billing Scene Scope

- `billingScenes` is the complete logical rVideo scene set for the initial operation, ordered by `SceneIndex`.
- `imageWorkScenes` is the separate image-generation work set and may be reduced by `input.SceneIds`, `OnlyMissingOrFailed`, `ShouldRenderScene`, and active-image filtering.
- `SceneIds` now scopes image work only. It cannot reduce initial VIDEO seconds or external VOICE count.
- `image_count` is calculated only from planned AI image generation scenes, so uploaded, reused, direct-reference, shared-reference, and already-selected images remain non-billable for IMAGE.
- `video_seconds` is calculated from every scene in `billingScenes`.
- `voice_count` is calculated from every scene in `billingScenes` when external voice is enabled.
- The parent billing snapshot keeps all logical video scene durations under `usagePlan.video.scene_durations`.

## Billing Operation Identity

- Chosen contract: `billingOperationId` is the complete logical billing identity for an initial rVideo operation.
- `billingOperationId` maps to the existing `core_job_id`; legacy flows fall back to the parent render job id only when no core job id exists.
- `parentRenderJobId` remains audit metadata in snapshots/events, not a matching key.
- Parent charge matching now requires the same `billingOperationId` and a valid `chargeReferenceId`.
- `RVIDEO_PARENT_BILLED` metadata remains complete: `billingOperationId`, `parentRenderJobId`, `projectId`, `serviceId`, `chargeReferenceId`, `imageCount`, `videoSeconds`, `voiceCount`, and `totalPoints`.
- Wallet idempotency continues to use the stable parent charge reference, so a retry of the same operation does not double debit.
- A later initial operation with a new `billingOperationId` is not suppressed by an older parent-billed event.

## rVideo Customer Point Preview

- Voice Mode now uses an explicit `ValueChanged` handler in `RenderVideoJobs.razor`.
- The handler normalizes the voice mode, clears the library catalog code when leaving Library mode, refreshes the initial point estimate immediately, and updates the UI.
- Library to None removes voice points from the preview without a page reload.
- None to Library recalculates voice count and increases the preview when external voice is configured.
- Backend revalidation remains authoritative: image-batch/video-batch handlers rebuild usage, resolve current pricing, read the wallet, and charge before child work is enqueued.

## Balance Notification

- Customer-scoped wallet notification remains in place from prior Point Module work.
- Wallet mutations notify the affected customer after commit.
- Voucher redemption still notifies after wallet mutation, redemption insert, voucher counter update, and transaction commit.
- Header reload remains customer-filtered in `MainLayout.razor`.

## Unrelated Branch Changes Present

- The branch also contains rVideo video fallback lifecycle changes from commit `1782dd1c5e7a53247f32869c339c7b1ae71ca79e`.
- Those fallback/provider changes were not modified as part of this final Point Module hardening task.
- This report does not treat provider fallback lifecycle behavior as part of the Point Module implementation.

## SQL Verification

- `TodoX.Web/database/manual/verify_point_module.sql` continues to cover billing operation id, parent charge reference, parent-billed events, `video_seconds`, `image_count`, `voice_count`, duplicate parent charge checks, voucher redemptions, and wallet/ledger balance comparison.
- No schema change, migration, or direct database execution was required.

## Tests

- `git diff --check`: PASS. Git reported CRLF conversion warnings only.
- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --filter "FullyQualifiedName~UnifiedPointModuleRegressionTests"`: PASS, 7 passed.
- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release`: FAIL with 5 existing unrelated failures, 875 passed:
  - `BillingAndRatioRegressionTests.RequestedRatioOverridesProviderRouteDefaults`
  - `FavoriteServicesRegressionTests.FavoriteAction_IsRenderedBesidePrimaryAction_NotOverThumbnail`
  - `DanceSellAi79ReferenceProviderTests.SubmitAsync_UsesVerifiedFashionTryOnFormPayload`
  - `RVideoAutosaveWorkflowTests.SceneGrid_IsTwoColumnsOnDesktopAndOneColumnNarrow`
  - `DanceSellPhase2ValidationTests.ReferencePrompt_MatchesTheVerified79AiTryOnPromptExactly`

## Build

- Initial attempt from `TodoX.Web`: `dotnet build TodoX.Dashboard.sln -c Release`: FAIL, solution file not found from that working directory.
- Correct run from repository root: `dotnet build TodoX.Dashboard.sln -c Release`: PASS with 45 existing Razor generated-code nullable warnings.

## Publish

- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`: PASS with 45 existing Razor generated-code nullable warnings.
- Output directory: `artifacts\publish\todox-dashboard`.

## Git Push

- `git push origin integration/rdance-on-construction-video-core`: PASS. The final pushed HEAD is `202fe444460b14cb34b6be5df1e4f66e91f9871f`; implementation commit is `39197e12eb64579f16f8b270e7d154d3097475e1`.

## Files Changed

- `TodoX.Web/Components/Pages/RenderVideoJobs.razor`
- `TodoX.Web/Services/Render/SceneImageBatchRenderHandler.cs`
- `TodoX.Web/Services/VideoRender/RVideoInitialPointEstimateService.cs`
- `TodoX.Web/Services/VideoRender/RVideoSceneAudioAutoChainService.cs`
- `TodoX.Web/Services/VideoRender/SceneVideoRenderHandler.cs`
- `TodoX.Web.Tests/UnifiedPointModuleRegressionTests.cs`
- `TodoX.Web/docs/unified-point-module-report.md`

## Remaining Limitations

- The customer point estimate is advisory; backend wallet/pricing validation remains authoritative.
- The full test suite still has 5 unrelated failures outside this Point Module hardening scope.
