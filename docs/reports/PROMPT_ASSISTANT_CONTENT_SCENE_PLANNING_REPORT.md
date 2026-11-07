# Prompt Assistant Content-Driven Scene Planning Report

Date: 2026-09-23
Branch: `feature/admin-job-monitor`

## Audit

- Before the change, the video workspace always requested a target duration and a fixed scene count, defaulting to seven.
- The Agent input did not distinguish user-provided source content from a creative brief.
- Generated JSON checks covered JSON syntax and numeric consistency, but not per-scene duration, narration/prompt fields, or `tts_rate` limits.
- Existing TodoX parsing already recognizes scene duration, image/video prompts, narration aliases, and `tts_rate`.

## Implementation

- Added the unchecked-by-default creative-mode checkbox. Unchecked mode treats the user's text as exact source narration, directs the Agent to preserve its wording and order, and asks it to segment on punctuation and semantic boundaries without a fixed scene count or a new hook/conclusion.
- Checked mode allows the Agent to create content and shows the target-duration selector. The Agent chooses scene count; it is instructed to get close to the target without violating per-scene limits.
- Both modes instruct the Agent to include scene purpose, duration, image prompt, motion prompt, narration, and `tts_rate`; durations must be 4-8 seconds and `tts_rate` must be 1.0-1.2, defaulting to 1.0.
- Added Prompt Assistant output validation for generated prompts attached to a video project. It requires non-empty scenes, valid per-scene duration, image/motion/narration fields, and valid `tts_rate`. Invalid output is persisted as a failed generation and is not made active.
- Existing JSON import behavior is not routed through the new output validator. Prompt history, active prompt persistence, reference-image handling, and the existing RVideo handoff remain in place.

## Changed Files

- `TodoX.Web/Components/Pages/RenderVideoJobs.razor`
- `TodoX.Web/Services/PromptAssistant/PromptAssistantEndpoints.cs`
- `TodoX.Web/Services/PromptAssistant/ServicePromptAssistantService.cs`
- `TodoX.Web/Services/PromptAssistant/PromptAssistantVideoOutputValidator.cs`
- `TodoX.Web.Tests/PromptAssistantEndpointsTests.cs`
- `TodoX.Web.Tests/PromptAssistantVideoOutputValidatorTests.cs`
- `TodoX.Web.Tests/RenderVideoJobsLayoutTests.cs`

No database or migration changes were made. Image generation, video generation, voice/audio implementation, providers, render, queue, worker, and billing implementations/contracts were not modified.

## Validation

- `git diff --check`: passed.
- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PromptAssistant|FullyQualifiedName~RenderVideoJobsLayout"`: passed, 58 tests.
- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RVideo"`: 90 passed, 2 failed. The failures are existing static-source assertions in `RVideoAutosaveWorkflowTests.SceneGrid_IsTwoColumnsOnDesktopAndOneColumnNarrow` (CSS text mismatch) and `StaticImageBillingPolicyRegressionTests.RVideoInitialEstimateWiresStaticImageBillingSetting` (expected source text not found). Neither failing test's source or related subsystem was changed.
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore -p:UseSharedCompilation=false /m:1`: passed, 0 errors, 46 warnings (including existing generated Razor nullable warnings and an obsolete formatter API warning in tests).
- Targeted whitespace verification passed for the changed UI, request builder, validator, and test files. Full `dotnet format` verification reports whitespace issues in the pre-existing `PersistAndReturnAsync` block of `ServicePromptAssistantService.cs`; that unrelated block was not reformatted.
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -p:UseSharedCompilation=false /m:1 -o D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`: passed. Output: `artifacts/publish/todox-dashboard`.

## Git

- Commit message: `feat: add content-driven prompt assistant scene planning`
- Implementation commit SHA: `df04cb8b13bc6fbfb2c6b043a7b32cf00d419edd`
- Report is committed separately so it can record the implementation SHA.
