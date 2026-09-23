# Prompt Assistant Unicode JSON Fix Report

## Scope

Fixed the Prompt Assistant JSON serialization representation for Unicode content. No database schema, migration, RVideo, image generation, video generation, TTS/audio, provider, render, queue, worker, or billing behavior was changed.

## Root Cause

The Gommo response was parsed with `JsonDocument`, then the service retained `RootElement.GetRawText()`. Later, reference-image enrichment serialized a `JsonNode` using the default `System.Text.Json` encoder. That encoder represented non-ASCII characters as `\\uXXXX`, which made valid Vietnamese text appear escaped in the generated prompt UI and downloaded JSON.

This was a serialization representation issue, not database corruption. JSONB persistence and the RVideo flow were not the source of the displayed escaping.

The fix distinguishes two cases:

- `\\u0110` in JSON is a valid Unicode escape and parses to the character `Đ`.
- `\\\\u0110` in JSON is a literal backslash sequence and parses to the text `\\u0110`; the fix deliberately preserves that content and does not rewrite prompt text.

## Solution

- Added `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` to the existing Prompt Assistant JSON options.
- Added `ServicePromptJson.Canonicalize`, which parses and reserializes JSON structurally with the configured options. No regex or string replacement is used.
- Canonicalized generated Agent JSON before persistence.
- Canonicalized imported JSON before reference enrichment.
- Kept the existing JSON schema and reference enrichment behavior unchanged.

## Changed Files

- `TodoX.Web/Services/PromptAssistant/ServicePromptAssistantModels.cs`
- `TodoX.Web/Services/PromptAssistant/ServicePromptAssistantService.cs`
- `TodoX.Web.Tests/ServicePromptAssistantTests.cs`
- `PROMPT_ASSISTANT_UNICODE_FIX_REPORT.md`

## Tests

Added coverage for:

- Vietnamese Unicode in `voice`, `image_prompt`, and `subtitle_lines`.
- Preservation of literal double-escaped Unicode.
- Preservation of Vietnamese Unicode through reference-image enrichment.
- Existing generation persistence JSON round-trip contract remains covered.

Results:

- `dotnet test TodoX.Web.Tests\\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ServicePromptAssistant|FullyQualifiedName~PromptAssistant|FullyQualifiedName~RenderVideoJobsLayout"`: **61 passed, 0 failed**.
- `dotnet test TodoX.Web.Tests\\TodoX.Web.Tests.csproj -c Release --no-restore --filter "PromptAssistant|TodoXVideoPromptParser|ServicePromptAssistant"`: **41 passed, 0 failed**.

## Build and Publish

- `dotnet build TodoX.Dashboard.sln -c Release --no-restore -p:UseSharedCompilation=false /m:1`: **succeeded, 0 errors**.
- `dotnet publish TodoX.Web\\TodoX.Web.csproj -c Release --no-restore -p:UseSharedCompilation=false /m:1 -o D:\\todoX\\Dashboard-web\\TodoXPortal\\todoX-Dashboard-SaaS\\artifacts\\publish\\todox-dashboard`: **succeeded**.

Existing compiler warnings remain in generated Razor code and a legacy test; none were introduced as errors by this change.

## Protected Areas Confirmed Untouched

No changes were made to RVideo/render/video generation, TTS/audio, provider payload contracts, queue, worker, billing, database schema, or migrations.

## Commit

Commit SHA: to be filled after commit.
