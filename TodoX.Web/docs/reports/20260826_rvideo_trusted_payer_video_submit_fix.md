# 20260826 RVIDEO Trusted Payer Video Submit Fix

## 1. Root Cause
Scene-video background execution was not consistently using a canonical persisted trusted payer context. The auto/manual enqueue paths and the worker path were each responsible for context construction, which let the worker fall back into session-dependent payer resolution and fail before `79AI /create-video`.

## 2. Exact Loss Point
The vulnerable path was `SceneVideoWorkerHandler.HandleAsync -> HandleProviderVideoAsync` before the fix, where billing reached `ReserveAsync` without revalidating against persisted RVIDEO ownership. The enqueue side also used non-canonical payer construction in `RenderVideoJobs.razor`.

## 3. AUTO Before/After
- Before: trusted payer context could be absent or non-canonical.
- After: `RVideoSceneVideoAutoChainService.TryEnqueueSceneVideoAsync` always builds and stores canonical `trustedPayerContext`.

## 4. MANUAL Before/After
- Before: manual enqueue used a generic background payer helper.
- After: `RenderVideoJobs.razor` uses `IRVideoTrustedPayerContextService.BuildRVideoTrustedPayerContextAsync(...)`.

## 5. RETRY Before/After
- Before: retry could reuse stale/null input context.
- After: retry enqueue goes through the same canonical builder, and the worker revalidates persisted ownership again before billing.

## 6. Canonical Payer Context
Added `TodoX.Web/Services/VideoRender/RVideoTrustedPayerContextService.cs`.
It validates:
- project exists
- scene belongs to project
- tenant matches
- core job exists
- core job operation type is `RVIDEO`
- customer ownership matches between project and core job

Mismatch throws `rvideo_video_payer_context_mismatch`.

## 7. Ownership/Security Validation
Trusted payer context is derived only from persisted server-side data. Client input is not trusted as the source of payer identity.

## 8. Billing Reserve Behavior
Scene-video billing still uses `ReserveAsync` first. Insufficient balance still blocks provider submit. No tariff or wallet changes were made.

## 9. Insufficient-Points Behavior
Video failure now says:
`Insufficient TodoX points for video generation. Required ..., available ....`
Image billing messages remain image-specific.

## 10. Error Message Changes
- `Cannot resolve billing payer for RVIDEO scene video...`
- image billing message unchanged for image flows

## 11. Events Added
Added RVIDEO diagnostic events around payer, reserve, provider submit, poll, and completion:
- `RVIDEO_VIDEO_BILLING_PAYER_RESOLVE_BEGIN`
- `RVIDEO_VIDEO_BILLING_PAYER_RESOLVED`
- `RVIDEO_VIDEO_BILLING_PAYER_FAILED`
- `RVIDEO_VIDEO_BILLING_RESERVE_BEGIN`
- `RVIDEO_VIDEO_BILLING_RESERVED`
- `RVIDEO_VIDEO_BILLING_RESERVE_FAILED`
- `RVIDEO_VIDEO_PROVIDER_RESOLVE_BEGIN`
- `RVIDEO_VIDEO_PROVIDER_RESOLVED`
- `RVIDEO_VIDEO_SOURCE_UPLOAD_BEGIN`
- `RVIDEO_VIDEO_SOURCE_UPLOAD_SUCCESS`
- `RVIDEO_VIDEO_SOURCE_UPLOAD_FAILED`
- `RVIDEO_VIDEO_SUBMIT_BEGIN`
- `RVIDEO_VIDEO_SUBMITTED`
- `RVIDEO_VIDEO_SUBMIT_FAILED`
- `RVIDEO_VIDEO_POLL_RESPONSE`
- `RVIDEO_VIDEO_COMPLETED`
- `RVIDEO_VIDEO_FAILED`

## 12. Lifecycle Changes
Existing `SyncLifecycleAsync` wiring was added at image/video start points:
- image render begin -> `RVideoStages.Image`
- video render begin -> `RVideoStages.Video`

`RVideoJobService` itself was not redesigned.

## 13. Files Changed
- `TodoX.Web/Components/Pages/RenderVideoJobs.razor`
- `TodoX.Web/Program.cs`
- `TodoX.Web/Services/AiProviders/AiBillingPayerResolver.cs`
- `TodoX.Web/Services/AiProviders/AiImageBillingService.cs`
- `TodoX.Web/Services/Render/SceneImageRenderWorkItemHandler.cs`
- `TodoX.Web/Services/VideoRender/RVideoSceneVideoAutoChainService.cs`
- `TodoX.Web/Services/VideoRender/RVideoTrustedPayerContextService.cs`
- `TodoX.Web/Services/VideoRender/SceneVideoRenderHandler.cs`
- `TodoX.Web/Services/VideoRender/SceneVideoWorkerHandler.cs`
- `TodoX.Web/Tests/RVideoProviderPollingRegressionTests.cs`
- `TodoX.Web/Tests/RVideoVideoHotfixTests.cs`

## 14. Focused Test Results
Passed:
- `dotnet test TodoX.Web\Tests\TodoX.Web.Phase1B.Tests.csproj --no-restore --filter "FullyQualifiedName~RVideoProviderPollingRegressionTests|FullyQualifiedName~RVideoVideoHotfixTests"`

## 15. Full Test Results
Failed due unrelated preexisting repo issues:
- `TodoX.Web.Tests.RDanceFashionDemoPageTests.*`
- `TodoX.Web.Tests.DanceSellAi79ReferenceProviderTests.SubmitAsync_UsesVerifiedFashionTryOnFormPayload`
- `TodoX.Web.Tests.DanceSellPhase2ValidationTests.ReferencePrompt_MatchesTheVerified79AiTryOnPromptExactly`

The RVIDEO-focused tests passed.

## 16. Build Result
Passed:
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`

## 17. Publish Result
Passed:
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`

Output:
- `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

## 18. 79AI Contract Verification
Unchanged:
- provider code: `79ai`
- capability: `rvideo_scene_video_generation`
- submit path: `/create-video`
- poll path: `/video`
- image upload path: `/image-upload`
- model: `veo_omni`

## 19. YEScale Verification
YEScale was not modified. I also checked the control plane with `yescale_list_models` for `veo_omni`; it returned no matches, which confirmed this fix did not depend on changing YEScale data.

## 20. Git Commit SHA
Implementation commit: `9d3de64`.

## 21. Branch Pushed
`integration/rdance-on-construction-video-core` is the target branch.
