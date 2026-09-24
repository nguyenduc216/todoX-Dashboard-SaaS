# Prompt Assistant Character Reference Synchronization Fix

Date: 2026-09-24

## Root cause

`RenderVideoJobs.razor` held the current Character Library or uploaded image in UI state, but Prompt Assistant generation and import only inferred character reference data from persisted `RVideoJobSettingsDto`. The persisted settings could lag behind the current selection, so generated JSON could keep `character_reference_mode = none` even while the UI displayed a selected reference image.

The previous `VideoPromptReferenceEnricher` only appended a generic instruction. It did not synchronize reference metadata, did not remove stale metadata/instructions, and did not update an already-active prompt when the character changed.

## Data flow

Before:

```text
UI character selection
  -> Prompt Assistant generation/import
  -> persisted project settings lookup
  -> generic image_prompt enrichment only
```

After:

```text
UI character selection (actual URL/object key + name/source)
  -> Prompt Assistant generation/import
  -> deterministic metadata + scene image_prompt synchronization
  -> active generation JSON + project original_prompt synchronized transactionally
```

Persisted settings remain a fallback for non-workspace callers.

## Behavior

- No reference sets `character_reference_mode` to `none`, clears reference image/metadata, and removes recognized stale reference wrappers.
- Character Library uses the selected record's `MasterImageUrl` or `MasterImageObjectKey`; the name remains metadata only.
- Uploaded character uses the uploaded `FileUrl` or `StorageKey`.
- The canonical reference contract is injected only when `main_character_present = true`.
- Character A to Character B removes A and writes only B.
- Character to no character removes reference metadata/instructions without changing unrelated prompt fields.
- No reference to character synchronizes the existing active prompt without AI regeneration.
- Selecting an older prompt from history synchronizes that prompt to the current character before it becomes active.
- Scenes without `image_prompt` remain structurally unchanged.
- `UseReferenceImageForAllScenes` is read but not modified; shared-reference rendering semantics are unchanged.

## Files changed

- `Components/Pages/RenderVideoJobs.razor`
- `Services/PromptAssistant/PromptAssistantCharacterReferenceSynchronizer.cs`
- `Services/PromptAssistant/ServicePromptAssistantService.cs`
- `Services/PromptAssistant/ServicePromptAssistantRepository.cs`
- `../TodoX.Web.Tests/PromptAssistantCharacterReferenceSynchronizerTests.cs`
- `docs/reports/20260924_prompt-assistant-character-reference-sync-fix.md`

## Validation

- Prompt Assistant, character synchronization, and workspace layout tests: 59 passed, 0 failed.
- Relevant RVideo test filter: 113 passed, 2 baseline failures unrelated to this change:
  - `StaticImageBillingPolicyRegressionTests.RVideoInitialEstimateWiresStaticImageBillingSetting` expects an obsolete source string; current `HEAD` uses `ResolveInitialImageCount`.
  - `RVideoAutosaveWorkflowTests.SceneGrid_IsTwoColumnsOnDesktopAndOneColumnNarrow` hard-codes LF while the existing CSS is CRLF; the expected rule is present in `HEAD`.
- C# whitespace lint for changed C# files: passed.
- `dotnet build ..\TodoX.Dashboard.sln -c Release --no-restore`: passed, 0 errors, 46 existing warnings.
- `git diff --check`: passed.
- `dotnet publish TodoX.Web.csproj -c Release --no-restore -o ..\artifacts\publish\todox-dashboard`: passed.
- Publish output: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`.

## Protected areas

No image/video provider, RVideo render handler, TTS/voice, queue/worker/ffmpeg, billing/points, provider routing/credentials, database schema/migration, or shared-reference rendering implementation was modified.

## Remaining risks

- Runtime database integration was not exercised against a live environment. Repository updates are tenant-, project-, and active-generation-scoped and transactional.
- Legacy free-form reference instructions that do not use a recognized wrapper cannot be removed safely without risking unrelated prompt content; canonical and existing known wrappers are covered.
