# RVIDEO VEO 3.1 Duration Resolution Fix

## Root Cause

`AiProviderModelRepository` read rows from `todox_ai_model_capability`, but did not merge the enabled capability `config_json` into `SupportedDurations`. It derived the runtime model options only from model `raw_json` and active price rows. For VEO 3.1, the production capability has `supported_durations: [4, 6, 8]`, while every active price row has `duration_seconds = NULL`. The resulting runtime duration contract was empty.

## Rejection Path

The rejection is backend resolver behavior, not a frontend duration-option validation:

1. `SceneVideoWorkerHandler.HandleProviderVideoAsync` calls `_models.GetModelsAsync(...)`.
2. It calls `EnrichCatalogDurationsAsync(...)`.
3. It calls `ResolveFallbackCandidateResolution(...)`.
4. `ResolveFallbackCandidateResolution` builds `supportedDurations` from `model.SupportedDurations` and rejects the VEO 3.1 candidate when `supportedDurations.Count == 0`, with diagnostic reason `catalog_duration_contract_missing`.
5. No VEO 3.1 candidate is returned to the fallback attempt loop. If no other candidate remains, the worker emits `RVIDEO_VIDEO_FALLBACK_EXHAUSTED`.

The literal Vietnamese UI/runtime string was not present in the source tree. The backend condition above is the proven source of the false unavailable state.

## Fix

Enabled model capability `config_json` is now normalized alongside `raw_json` and prices for both model-list hydration and `GetModelByCodeAsync`. Thus `supported_durations` is authoritative for capability availability. Generic price rows continue to add mode/resolution/ratio but never invent a duration.

Price lookup now uses `AiPricingEngine.FindPrice`:

1. exact `mode + resolution + duration + ratio`
2. matching generic row with `duration_seconds IS NULL`

An exact duration row wins over a generic row. A generic price cannot make an unsupported capability duration valid.

## Result Matrix

| Requested duration | VEO 3.1 result |
| --- | --- |
| 4s | valid, provider duration 4s |
| 6s | valid, provider duration 6s |
| 8s | valid, provider duration 8s |
| 10s | invalid for VEO 3.1; both `fast` and `lite` are skipped and the existing next policy candidate, such as Grok 10s, remains eligible |

Fallback policy ordering, provider routing, 79AI create payload, persistence, polling, and reconciliation logic were not changed.

## Files Changed

- `TodoX.Web/Services/AiProviders/AiProviderModelOptionsNormalizer.cs`
- `TodoX.Web/Services/AiProviders/AiProviderModelRepository.cs`
- `TodoX.Web/Services/AiProviders/AiPricingEngine.cs`
- `TodoX.Web/Services/AiProviders/AiPricingService.cs`
- `TodoX.Web.Tests/AiProviderDurationPricingTests.cs`
- `TodoX.Web/Tests/AiPricingEngineTests.cs`
- `TodoX.Web/Tests/RVideoVideoHotfixTests.cs`

## Validation

- `dotnet format TodoX.Dashboard.sln whitespace --verify-no-changes --no-restore --include ...`: passed.
- `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~AiProviderDurationPricingTests|FullyQualifiedName~AiPricingEngineTests|FullyQualifiedName~RVideoVideoHotfixTests"`: passed, 17/17.
- Expanded RVIDEO filter: 90 passed, 2 unrelated existing source-assertion failures in `RVideoAutosaveWorkflowTests.SceneGrid_IsTwoColumnsOnDesktopAndOneColumnNarrow` and `StaticImageBillingPolicyRegressionTests.RVideoInitialEstimateWiresStaticImageBillingSetting`.
- `dotnet build TodoX.Web/TodoX.Web.csproj --configuration Release --no-restore`: passed, 0 errors. It reports 45 existing generated Razor nullable warnings.
- `dotnet publish TodoX.Web/TodoX.Web.csproj --configuration Release --no-build --output artifacts/publish/todox-dashboard`: passed.

## Publish

Artifact published to `artifacts/publish/todox-dashboard`.

## Commit

Implementation commit: `e19546b3645f347be358fabef0a04efb1b238c19`.
