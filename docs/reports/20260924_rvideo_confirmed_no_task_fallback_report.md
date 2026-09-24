# RVIDEO Confirmed No-Task Fallback Report

Date: 2026-09-24

## Scope

This change is limited to RVIDEO submit-outcome classification, confirmed-no-task fallback behavior, sanitized diagnostics, and focused regression coverage. It does not change Grok policy or mode, provider-duration adaptation, final merge/FFmpeg behavior, billing architecture, polling or reconciliation worker policy, provider endpoints or credentials, image/audio/caption generation, RDance, Timelapse, UI, database schema, or migrations.

## Root Cause

79AI can return HTTP 200 with parsed provider JSON containing an explicit rejection, `countTasks = 0`, and no accepted task identifier. `Ai79TaskClient` correctly preserves that sanitized response but may normalize the exception-level error code to `provider_error`. The worker previously recognized only the exact `NOT_RESOURCES` shape as proven safe for fallback. A generic parsed rejection was therefore treated as an ambiguous submit, marked `pending_reconciliation`, and stopped before the next model candidate.

## New Submit-Outcome Rule

A submit is now classified as definitely not submitted only when all of these conditions hold:

1. An HTTP response was received and its status is 2xx.
2. The sanitized response is valid provider JSON.
3. No accepted `task_id`, `taskId`, `id_base`, `video_id`, or `videoId` exists anywhere in the response.
4. `countTasks` or `count_tasks` is explicitly present and equals zero.
5. The response contains non-empty explicit rejection evidence in an error field, a non-success rejection code, or a recognized rejection/unavailability message.

Exact `NOT_RESOURCES` responses retain the existing `KNOWN_NO_RESOURCES` classification. Other confirmed no-task rejections use `PROVIDER_REJECTED_NO_TASK`. Both continue through the existing candidate fallback and billing failure/release path.

## Duplicate-Submit Safety

Immediate fallback remains disabled when a provider task may exist. Task identifiers take precedence over error fields, including nested and numeric identifiers. `countTasks > 0`, missing `countTasks`, malformed or empty JSON, success-like `code = 200`, HTTP 200 without explicit rejection evidence, transport timeout/reset, and other uncertain shapes remain ambiguous and continue to `pending_reconciliation`.

## Fallback Flow

The existing order is unchanged:

1. `79ai / veo_omni / flash`
2. `79ai / veo_3_1 / fast`
3. `79ai / veo_3_1 / lite`
4. `79ai / grok_video_heavy / no explicit mode`

A confirmed-no-task failure terminalizes only the current candidate, records `submitOutcome = definitely_not_submitted`, and advances to the next candidate. The existing Grok duration adaptation remains unchanged, including scene duration 8 seconds resolving to provider duration 10 seconds.

## Diagnostics

Confirmed-no-task events now retain sanitized request/response JSON and record the failure classification, HTTP/provider error details, `countTasks = 0`, `hasAcceptedTaskId = false`, fallback availability, and next provider/model/mode. Ambiguous submits record `submitOutcome = ambiguous` and continue to reconciliation without another provider submit.

## Changed Files

- `TodoX.Web/Services/VideoRender/SceneVideoWorkerHandler.cs`
- `TodoX.Web/Tests/RVideoVideoHotfixTests.cs`
- `TodoX.Web.Tests/Ai79TaskClientTests.cs`
- `docs/reports/20260924_rvideo_confirmed_no_task_fallback_report.md`

## Validation

- Focused worker regression tests: 17 passed, 0 failed.
- Focused `Ai79TaskClient` response-preservation tests: 2 passed, 0 failed.
- `RVideoVideoHotfixTests` plus `RVideoProviderPollingRegressionTests`: 222 passed, 7 pre-existing failures. The failures are unchanged brittle/reflection/source-text assertions covering manual rerender text, two parameter-count mismatches, same-model source text, a newline-sensitive billing assertion, stale `route.ModelName` text, and an overbroad authorization-string assertion.
- Full `Ai79TaskClientTests`: 49 passed, 2 pre-existing failures in legacy video `request_id` acceptance tests. `Ai79TaskClient` production code was not changed by this task.
- Changed-file whitespace verification with `dotnet format`: passed.
- `dotnet build TodoX.Web.csproj -c Release --no-restore -p:UseSharedCompilation=false`: passed with existing generated Razor nullable warnings.
- `dotnet publish TodoX.Web.csproj -c Release --no-restore -p:UseSharedCompilation=false -o D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`: passed.
- `git diff --check`: passed.

No database update or migration is required.
