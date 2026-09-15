# RVIDEO Duration Fallback and Scene Override Report

## Scope

This change finishes the RVIDEO duration fallback and per-scene rerender override prompt. It only touches scene-video fallback selection and the RVideo Jobs page's scene-level model/duration selection. It does not change provider identifier persistence, id_base polling, provider endpoints, provider payload fields, pending reconciliation, normal poll timeout, billing formula, render-job claim SQL, or non-RVIDEO protected subsystems.

## Root Cause

The previous implementation had two regressions after adding duration-first fallback:

1. Same-model 8s candidates were inserted into the normal candidate list before any provider failure was known. That meant a normal provider capacity failure such as `NOT_RESOURCES` could route from `veo_omni/flash/4s` to `veo_omni/flash/8s`, even though duration fallback is only safe when the provider explicitly rejects the duration.
2. Restart recovery relied on the numeric `fallback-N` suffix as the source of truth. Historical rows such as `fallback-1` could be reinterpreted after the candidate sequence changed, so a previously persisted `veo_3_1/fast/4s` attempt could map to the wrong current candidate.

The UI rerender path also defaulted untouched scene dropdown state back to the initial policy, so a no-change rerender could silently submit Omni/flash instead of the scene's latest effective model/mode/duration.

## Final Fallback Chain

Configured model order remains:

1. `79ai / veo_omni / flash`
2. `79ai / veo_3_1 / fast`
3. `79ai / veo_3_1 / lite`
4. `79ai / grok_video_heavy / normal`

The initial candidate resolver now returns policy-order candidates only. Same-model higher-duration fallback is created dynamically only when the failure classification is `PROVIDER_DURATION_REJECTED`.

## Candidate Resolution

With the regression-test catalog:

| Requested duration | Initial candidates |
| --- | --- |
| 4s | Omni 4, VEO 3.1 fast 4, VEO 3.1 lite 4, Grok 6 |
| 6s | Omni 6, VEO 3.1 fast 6, VEO 3.1 lite 6, Grok 6 |
| 8s | Omni 8, VEO 3.1 fast 8, VEO 3.1 lite 8, Grok 10 |
| 10s | Omni 10, Grok 10; VEO 3.1 candidates are skipped because its capability durations stop at 8s |

Each candidate is resolved against that candidate's own model/mode capability. The original input mode is not used to filter every fallback candidate.

## Failure-Classified Duration Transition

If a provider returns a duration rejection:

- `veo_omni/flash/4s` -> `veo_omni/flash/8s`
- `veo_omni/flash/6s` -> `veo_omni/flash/8s`
- `veo_3_1/fast/4s` -> `veo_3_1/fast/8s`
- `veo_3_1/lite/4s` -> `veo_3_1/lite/8s`

If no higher same-model duration is available, fallback continues to the next configured model candidate.

## NOT_RESOURCES Behavior

`NOT_RESOURCES` remains classified as `KNOWN_NO_RESOURCES`. It skips same-model duration fallback and moves to the next non-duration fallback candidate:

- Omni 4 `NOT_RESOURCES` -> VEO 3.1 fast 4
- VEO 3.1 fast 4 `NOT_RESOURCES` -> VEO 3.1 lite 4

## Unknown Submission Behavior

Unknown submit states, timeout states, and ambiguous provider outcomes still move to `pending_reconciliation`. They do not submit duplicate provider tasks and do not model-fallback speculatively.

## Historical Candidate Compatibility

Restart recovery now reads persisted candidate identity from `render_config_json` and maps by provider/model/mode/providerDurationSeconds/providerResolution rather than trusting only the `fallback-N` suffix. If a historical candidate is missing from the current initial sequence, it is restored into the candidate list at the historical position and logs `RVIDEO_VIDEO_HISTORICAL_CANDIDATE_RESOLVED`. Unmappable persisted versions log `RVIDEO_VIDEO_HISTORICAL_CANDIDATE_MISMATCH`.

## Scene UI Latest-Version Default

The RVideo Jobs scene UI now distinguishes an intentional user override from an untouched dropdown default. When untouched, it prefers the latest scene-video version's persisted candidate:

- provider/model/mode from `render_config_json`
- `providerDurationSeconds`
- fallback to `requestedDurationSeconds`
- fallback to the version duration
- final fallback to initial policy only when no usable latest candidate exists

A no-change manual rerender keeps the latest effective model/mode/duration instead of silently reverting to Omni/flash.

## Files Changed

- `TodoX.Web/Services/VideoRender/SceneVideoWorkerHandler.cs`
- `TodoX.Web/Components/Pages/RenderVideoJobs.razor`
- `TodoX.Web/Tests/RVideoVideoHotfixTests.cs`
- `artifacts/reports/RVIDEO_DURATION_FALLBACK_SCENE_OVERRIDE_REPORT.md`

## Validation

Targeted tests:

- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~RVideoVideoHotfixTests|FullyQualifiedName~RVideoSceneVideoRecoveryAndDiagnosticsTests"`: passed, 27 passed, 0 failed.
- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~RVideo|FullyQualifiedName~79Ai|FullyQualifiedName~VideoRender"`: 118 total, 115 passed, 3 failed. The failures are pre-existing unrelated source assertions in `StaticImageBillingPolicyRegressionTests.RVideoInitialEstimateWiresStaticImageBillingSetting`, `DanceSellPhase2ValidationTests.ReferencePrompt_MatchesTheVerified79AiTryOnPromptExactly`, and `RVideoAutosaveWorkflowTests.SceneGrid_IsTwoColumnsOnDesktopAndOneColumnNarrow`.

Build:

- `dotnet build TodoX.Web\TodoX.Web.csproj --configuration Release --no-restore`: passed, 0 errors, 45 existing Razor nullable warnings.

Publish:

- `dotnet publish TodoX.Web\TodoX.Web.csproj --configuration Release --no-build --output artifacts\publish\todox-dashboard`: passed.
- Output directory: `artifacts/publish/todox-dashboard`.

Diff hygiene:

- `git diff --check`: passed.
- Changed-file scan for replacement character: clean.

Commit:

- Implementation commit: `d64d8e1e8e81dfc10ea524ff13877f2de35194b2`.

## Remaining Risks

- Production catalog data must continue to expose the intended capability durations.
- Existing unrelated failures in broad filters remain outside this prompt.
