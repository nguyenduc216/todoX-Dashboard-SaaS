# RVIDEO Duration Fallback and Per-Scene Override Report

## Scope

This change completes the existing RVIDEO scene-video duration fallback and adds per-scene model/mode/duration selection for single-scene render and rerender. Provider routing, payload fields, endpoint, normal retry policy, identifier persistence, polling semantics, reconciliation, and protected non-RVIDEO subsystems were left unchanged.

## Root Cause

The existing scene-video worker resolved a candidate only from the requested scene duration. When a provider rejected that duration, the worker moved directly to the next model policy. It had no same-model higher-duration candidate and no explicit duration-rejection classification.

The per-scene UI also had no catalog-backed model/duration controls, and the selected values were not propagated into the scene child input. As a result, a single-scene rerender could not request a catalog-supported model/mode/duration override.

## Final Fallback Behavior

The configured policy order remains:

1. `79ai / veo_omni / flash`
2. `79ai / veo_3_1 / fast`
3. `79ai / veo_3_1 / lite`
4. `79ai / grok_video_heavy / normal`

For a duration rejection, the worker first tries the same model at the next supported higher duration when that candidate is supported. The current implementation specifically adds `8` after a resolved `4` or `6` candidate when `8` is in that model's capability duration set. Model fallback continues only after that candidate fails or is not available.

`NOT_RESOURCES` remains classified independently and keeps the existing safe fallback behavior.

## Candidate Resolution

With the regression-test catalog:

| Requested duration | Resolved candidate durations |
| --- | --- |
| 4s | Omni 4, Omni 8, VEO 3.1 fast 4, fast 8, lite 4, lite 8, Grok 6 |
| 6s | Omni 6, Omni 8, VEO 3.1 fast 6, fast 8, lite 6, lite 8, Grok 6 |
| 8s | Omni 8, VEO 3.1 fast 8, lite 8, Grok 10 |
| 10s | Omni 10, Grok 10; VEO 3.1 candidates are skipped when their capability duration set does not support 10s |

Each candidate is resolved against its own catalog model and mode. The input mode is not used to filter every fallback candidate.

## Per-Scene Override

`RenderVideoJobs.razor` now loads enabled, user-selectable, non-deprecated video model options from the provider catalog and uses `SupportedDurations` for the duration selector.

For a single-scene render/rerender, the selected values are passed as:

- `ManualOverride = true`
- `RequestedModelCode`
- `RequestedMode`
- `RequestedDurationSeconds`

The handler validates the selected model, mode, and duration against the catalog before creating the child job. Invalid overrides are rejected and logged as a project event. Existing scene versions are not deleted; the existing version/reuse inputs remain available for retry/recovery flows.

## Billing

The child-job estimate uses the requested override duration when present. Existing candidate/provider-duration billing logic remains unchanged.

## Files Changed

- `TodoX.Web/Components/Pages/RenderVideoJobs.razor`
- `TodoX.Web/Services/VideoRender/SceneVideoRenderHandler.cs`
- `TodoX.Web/Tests/RVideoVideoHotfixTests.cs`
- `artifacts/reports/RVIDEO_DURATION_FALLBACK_SCENE_OVERRIDE_REPORT.md`

## Tests

Passed:

- `dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~RVideoVideoHotfixTests|FullyQualifiedName~RVideoSceneVideoRecoveryAndDiagnosticsTests"`: 27 passed, 0 failed.
- `dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~AiProviderDurationPricingTests|FullyQualifiedName~AiPricing|FullyQualifiedName~RVideoVideoHotfixTests"`: 17 passed, 0 failed.

The broader filter `FullyQualifiedName~RVideo|FullyQualifiedName~79Ai|FullyQualifiedName~VideoRender` ran 118 tests: 115 passed and 3 failed in pre-existing unrelated source assertions for static-image billing, DanceSell, and page CSS. No RVIDEO duration/fallback test failed.

## Build and Publish

- Release build: passed, 0 errors; existing Razor nullable warnings remain.
- Publish command:
  `dotnet publish TodoX.Web\TodoX.Web.csproj --configuration Release --no-build --output artifacts\publish\todox-dashboard`
- Publish result: passed to `artifacts/publish/todox-dashboard`.

## Commit

Commit SHA: `9ce7216`

## Remaining Risks

- Production catalog data must expose correct capability `SupportedDurations`; unsupported durations are intentionally not fabricated.
- Same-model duration fallback currently targets the configured higher `8s` option for requests resolved to `4s` or `6s`.
- The three unrelated failures in the broad test filter remain outside this change.
