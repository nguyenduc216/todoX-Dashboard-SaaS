# Service Prompt Assistant Report

Date: 2026-09-19
Baseline commit: `991eb650e3771b94f4975b0744eb11b7bc2d043f`

## Scope

Implemented service-level Prompt Assistant for:

`natural language request -> configurable 79AI-compatible provider -> JSON parse -> recursive structure validation -> bounded repair -> preview/history persistence`

The implementation supports per-service configuration, draft/published/archived training versions, JSON template upload, structure-description upload, playground generation, validation errors, token usage, copy-to-clipboard, and generation history.

## Changed Files

- `Components/Pages/Services.razor`
- `Components/Dialogs/ServicePromptAssistantDialog.razor`
- `Program.cs`
- `appsettings.json`
- `Services/PromptAssistant/ServicePromptAssistantModels.cs`
- `Services/PromptAssistant/ServicePromptCompiler.cs`
- `Services/PromptAssistant/ServicePromptOutputParser.cs`
- `Services/PromptAssistant/ServicePromptStructureValidator.cs`
- `Services/PromptAssistant/ServicePrompt79AiClient.cs`
- `Services/PromptAssistant/ServicePromptAssistantRepository.cs`
- `Services/PromptAssistant/ServicePromptAssistantService.cs`
- `database/manual/service-prompt-assistant/20260919_service_prompt_assistant.sql`
- `../TodoX.Web.Tests/ServicePromptAssistantTests.cs`

## Database

Added a standalone manual SQL script for:

- `settings.service_prompt_assistants`
- `settings.service_prompt_training_versions`
- `settings.service_prompt_generations`
- required indexes and constraints

The script is additive, includes an idempotent active-version foreign-key guard, and was not executed. No migration was created or modified. No database update is required until the user manually reviews and runs this script.

## Provider and Security

- Provider URL, provider code, model code, timeout, and limits are configurable.
- Credentials are resolved through the existing `IProviderCredentialResolver`.
- No credential, access key, model ID, or secret was added to source/configuration.
- Stored request/response data is sanitized; provider secrets are replaced before persistence.
- YEScale MCP was queried for provider `79ai`; it returned zero model records. Therefore no YEScale model ID or capability was invented, and the provider/model remain configuration-driven.

## Validation and Repair

- JSON markdown fences are stripped before parsing.
- Invalid JSON is converted to a controlled provider error.
- Validation recursively checks required fields, unknown fields, JSON types, nested objects, and array item structure.
- Array length remains variable.
- Repair attempts are bounded to configured values clamped to 0-3.
- Prompt, completion, and total token usage are aggregated across initial generation and repairs.

## Protected Subsystems

Untouched:

- image, video, and voice generation
- render workers, handlers, queues, schedulers, and finalizers
- provider submit/poll logic
- ffmpeg
- billing, points, retry behavior
- RDance, Timelapse, and RVideo production flows

The only existing page edited was `Components/Pages/Services.razor`, which received the Prompt Assistant action.

## Validation Results

Passed:

```text
dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ServicePromptAssistantTests
6 passed, 0 failed

dotnet build ..\TodoX.Dashboard.sln -c Release --no-restore -p:UseSharedCompilation=false
0 errors, 46 warnings

dotnet publish TodoX.Web.csproj -c Release --no-restore -p:UseSharedCompilation=false -o ..\artifacts\publish\todox-dashboard
success
```

Publish output:

`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

Full test suite:

```text
dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore
950 passed, 22 failed, 0 skipped
```

The 22 failures are existing unrelated source-contract/regression failures in RDance, Timelapse, render, billing, and provider behavior. No protected production behavior was changed to address them.

Lint:

```text
dotnet format TodoX.Web.csproj --no-restore --verify-no-changes
failed
```

The command reports pre-existing whitespace violations across unrelated repository files, including protected media/render modules. Those files were not modified.

Additional checks:

- `git diff --check`: passed
- Changed-file mojibake/replacement-character scan: passed
- No database command was executed
- No commit or push was performed
- No production service was restarted or deployed
