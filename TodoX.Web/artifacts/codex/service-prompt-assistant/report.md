# Prompt Assistant Phase 1 Report

Date: 2026-09-21
Branch: `feature/admin-job-monitor`
Commit: `7708ea18e2bbfc05b60d4bb37fdd560b2815fe8b`

## Scope Completed

- Dashboard Prompt Assistant configuration for Gommo Agent.
- Runtime endpoint: `https://api.gommo.net/api/v2/chat`.
- `Gommo-Token` resolved through the existing credential abstraction; no secret is stored or returned to the UI.
- Runtime payload uses `GommoAgentIdBase`, fresh user/assistant UUIDs, and only the natural-language user request.
- SSE reads `data:` events, aggregates only `choices[0].delta.content`, ignores thinking/usage as output, supports `[DONE]`, timeout, bounded diagnostics, auth/error mapping, and sanitization.
- Final response JSON parsing and root/scenes validation, with optional consistency checks for `scene_count`, `duration`, and `total_shot_count`.
- Playground shows final JSON, output tokens, credit, timing, and supports copy/download.
- History reads provider and runtime metrics.
- One generation performs one provider call; legacy training/compiler/validator backend remains available but training UI is hidden for Phase 1.

## Changed Files

- `TodoX.Web/Components/Dialogs/ServicePromptAssistantDialog.razor`
- `TodoX.Web/Services/PromptAssistant/ServicePrompt79AiClient.cs`
- `TodoX.Web/Services/PromptAssistant/ServicePromptAssistantModels.cs`
- `TodoX.Web/Services/PromptAssistant/ServicePromptAssistantRepository.cs`
- `TodoX.Web/Services/PromptAssistant/ServicePromptAssistantService.cs`
- `TodoX.Web/Services/PromptAssistant/ServicePromptOutputParser.cs`
- `TodoX.Web.Tests/ServicePromptAssistantTests.cs`
- `TodoX.Web/database/manual/service-prompt-assistant/20260921_phase1_generation_metrics.sql`

## Validation Results

- Build: passed, 0 errors.
- `ServicePromptAssistantTests`: passed, 10/10.
- `GommoPromptAssistantTests`: passed, 1/1 after moving the test into `TodoX.Web.Tests`. Live Gommo acceptance was not run without a configured credential.
- `git diff --check`: passed.
- Publish: passed to `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`.

## Database Prerequisite

`20260921_phase1_generation_metrics.sql` is a manual, unexecuted SQL prerequisite. It makes `training_version_id` nullable for Phase 1 runtime generations and adds runtime metric columns. No migration was created or executed, and no database was modified.

## Protected Areas

Untouched: RVideo/render pipeline, image/video/audio/voice generation, workers, ffmpeg, mux/finalizer, RDance, Timelapse, Dance Sell, billing/points, and provider pipelines outside Prompt Assistant.