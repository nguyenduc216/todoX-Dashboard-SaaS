# Service Prompt Assistant Phase 1 Report

## Summary

Phase 1 of the Service Prompt Assistant configuration is complete. The implementation covers assistant configuration, training versions, publishing, prompt execution through 79AI, validation, repair metadata, usage reporting, and the related dashboard dialog.

## Scope

Included:

- Service-scoped prompt assistant configuration and version management.
- Draft validation and safe publishing workflow.
- 79AI chat-completions integration using configuration.
- Upload limits, prompt template validation, and controlled provider errors.
- Dashboard configuration, playground, history, and usage display.
- Standalone database hardening SQL.
- Regression tests for Phase 1 validation and provider configuration.

Frozen and untouched:

- Image generation, video generation, voice/audio, point/billing.
- RVideo, render, RDance, and Timelapse behavior.
- Existing public APIs outside the requested assistant scope.

## Baseline

- Branch: `feature/admin-job-monitor`
- Baseline commit: `4dba6072c52cc8b444896bc2e74289e4c8a332b9`
- Existing worktree changes were preserved.

## Architecture

The dashboard uses the existing Service Prompt Assistant repository, service, provider client, models, and Blazor dialog boundaries. Configuration is read through the existing options path. Persistence remains service- and assistant-scoped, with publishing handled transactionally by the repository.

## Code Changes

- `ServicePromptAssistantRepository.PublishAsync` validates ownership and draft state, archives the previous published version, publishes the requested draft, and updates the active version in one transaction.
- Domain failures use `ServicePromptDomainException`.
- Service validation covers version names, templates, descriptions, filenames, content, and configured byte limits.
- The 79AI client reports controlled errors when the provider response lacks choices, messages, or content.
- The dialog now exposes configuration, versions, uploads, playground, history, validation, repair attempts, token usage, and latency.
- Tests cover validator edge cases, configured endpoint and limits, invalid URLs, and missing provider content.

## Publish Fix

Publishing requires the requested version to belong to the selected assistant and service, requires `DRAFT` state, checks affected rows, and performs archive/publish/active-version updates transactionally.

## 79AI Config

The configured default endpoint is:

`https://79ai.net/api/chat/completions`

Credentials are not stored in source, configuration, tests, logs, or this report.

## Prompt Flow

The configured service prompt template is validated before execution. The assistant sends the resulting prompt through the configured provider client, captures the provider response, and records the existing generation/history metadata used by the dashboard.

## Validation

Validation is applied to version names, prompt templates, descriptions, filenames, and upload content. Limits are read from configuration rather than hard-coded in the UI.

## JSONB Persistence Fix

Incident: PostgreSQL `42804` occurred while saving `request_snapshot_sanitized` in `settings.service_prompt_generations`.

Root cause: .NET string parameters were inserted into PostgreSQL `jsonb` columns without explicit casts.

Fix:

- `RequestSnapshot` is inserted with `CAST(@RequestSnapshot AS jsonb)`.
- `GeneratedJson` is inserted with `CAST(@GeneratedJson AS jsonb)`.
- `ValidationErrors` is inserted with `CAST(@ValidationErrors AS jsonb)`.
- `GeneratedJson = NULL` remains accepted by PostgreSQL through the cast.
- `RequestSnapshot`, `ValidationErrors`, and non-null `GeneratedJson` are parsed before the database connection is opened; malformed JSON is rejected instead of being stored.

Database migration required: **NO**.

Schema changed: **NO**.

Render pipeline changed: **NO**.

## Repair

The UI exposes validation and repair-attempt metadata returned by the assistant flow. Repair behavior remains within the Service Prompt Assistant scope and does not alter protected media or billing contracts.

## Token Usage

The playground and history views display token usage and latency when supplied by the existing generation metadata.

## UI

The Service Prompt Assistant dialog was restored to readable ASCII labels and includes configuration, training versions, upload previews, playground execution, history, validation status, repair attempts, token usage, and latency.

## Database Update/Status

The standalone SQL file adds composite uniqueness and foreign-key constraints needed to ensure active versions and generations remain associated with the correct assistant and service.

Status: **NOT EXECUTED**. The current connection string targets a shared host whose environment could not be safely identified as development. No migration was created, modified, or applied, and no SQL was executed against a database.

## Tests

Targeted tests:

`dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ServicePromptAssistantTests`

Result: **14 passed, 0 failed** after adding the JSONB persistence regression tests.

Full test suite:

Result: **958 passed, 22 failed**. The baseline before this change was **954 passed, 22 failed**; the four additional tests are the JSONB persistence regression tests. The remaining failures are in pre-existing RDance, Timelapse, RVideo, render, billing, and provider regression areas and were not changed.

## Build

Command:

`dotnet build TodoX.Dashboard.sln -c Release --no-restore -p:UseSharedCompilation=false`

Result: **Success, 0 errors, 46 warnings**.

## Publish

Command:

`dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -p:UseSharedCompilation=false -o artifacts/publish/todox-dashboard`

Result: **Success**.

Output:

`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

## Render Pipeline Safety Verification

No RVideo, render, image generation, video generation, voice/audio, RDance, Timelapse, or billing production files were changed. The unrelated baseline failures in those areas were not repaired as part of this task.

## Security

- No credentials, access keys, or environment-specific secrets were added.
- Provider endpoint remains configuration-driven.
- Database SQL remains a manual, standalone script.
- Existing user changes were preserved.

## Changed Files

- `TodoX.Web.Tests/ServicePromptAssistantTests.cs`
- `TodoX.Web/Components/Dialogs/ServicePromptAssistantDialog.razor`
- `TodoX.Web/Services/PromptAssistant/ServicePrompt79AiClient.cs`
- `TodoX.Web/Services/PromptAssistant/ServicePromptAssistantModels.cs`
- `TodoX.Web/Services/PromptAssistant/ServicePromptAssistantRepository.cs`
- `TodoX.Web/Services/PromptAssistant/ServicePromptAssistantService.cs`
- `TodoX.Web/appsettings.json`
- `TodoX.Web/database/manual/service-prompt-assistant/20260919_service_prompt_assistant.sql`
- `TodoX.Web/artifacts/codex/service-prompt-assistant/report.md`

## Known Limitations

- `dotnet format TodoX.Web/TodoX.Web.csproj --no-restore --verify-no-changes` reports many pre-existing whitespace violations across unrelated and protected files.
- The full test suite still contains the baseline protected-subsystem failures.
- The database hardening SQL requires explicit manual review and execution in the intended environment.

## Git

The requested commit message is:

`fix: complete service prompt assistant phase one`

The final commit SHA and remote push verification are reported with the completed task.

## Next Phase

Phase 2 can add operational workflows and deeper provider integration after the manual database review and deployment-environment confirmation.
