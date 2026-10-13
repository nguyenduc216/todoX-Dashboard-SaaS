# RVID-PROMPT-ASSISTANT-VALIDATION-REPAIR.1

Approved scope: Prompt Assistant request assembly, output validation/repair,
reference normalization guard, diagnostics and regression tests.

Evidence: RunGenerationRepairLoopAsync previously returned immediately for
syntactically valid JSON. Video output validation ran after the repair loop,
so missing scenes and empty production prompts never consumed the configured
repair budget. Attempt counts also stayed zero when repair threw before returning.
The origins of the two reported production outputs remain unverified without
their sanitized raw/assembled responses and the agent instructions.

Changes:
- Require non-empty production content in both source and creative requests.
- Validate production fields inside the bounded repair loop for video projects.
- Repair only rejected fields and retain valid voice, scene metadata and references.
- Count repair calls independently of optional diagnostics, including failed calls.
- Log model output, parser, validation, normalization and repair stages/reasons.
- Classify empty/whitespace provider content as missing_content.
- Do not use reference instructions to fill an empty image prompt, and retain
  existing reference-prefixed text when no SCENE marker exists.

Changed files:
- TodoX.Web/Services/PromptAssistant/PromptAssistantEndpoints.cs
- TodoX.Web/Services/PromptAssistant/ServicePromptAssistantService.cs
- TodoX.Web/Services/PromptAssistant/ServicePromptOutputParser.cs
- TodoX.Web/Services/PromptAssistant/PromptAssistantCharacterReferenceSynchronizer.cs
- TodoX.Web.Tests/ServicePromptAssistantJsonRepairTests.cs
- TodoX.Web.Tests/PromptAssistantProductionRepairTests.cs
- This history file.

Verification (repository root unless noted):
- From TodoX.Web: dotnet test ../TodoX.Web.Tests/TodoX.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~PromptAssistant|FullyQualifiedName~ServicePromptAssistant" --verbosity quiet
  PASS: 100 tests, zero failures. A new test caught a conditional callback increment
  bug during development; it was fixed and the final run passed.
- From TodoX.Web: dotnet format whitespace TodoX.Web.csproj --no-restore --verify-no-changes --include Services/PromptAssistant/ServicePromptAssistantService.cs Services/PromptAssistant/PromptAssistantEndpoints.cs Services/PromptAssistant/ServicePromptOutputParser.cs Services/PromptAssistant/PromptAssistantCharacterReferenceSynchronizer.cs
  PASS (exit 0).
- From TodoX.Web: dotnet format whitespace ../TodoX.Web.Tests/TodoX.Web.Tests.csproj --no-restore --verify-no-changes --include ../TodoX.Web.Tests/PromptAssistantProductionRepairTests.cs ../TodoX.Web.Tests/ServicePromptAssistantJsonRepairTests.cs
  PASS (exit 0).
- git diff --check: PASS.
- dotnet build TodoX.Web/TodoX.Web.csproj -c Release --verbosity quiet
  PASS: zero errors, 46 existing warnings.
- dotnet publish TodoX.Web/TodoX.Web.csproj -c Release -o artifacts/publish/todox-dashboard --verbosity quiet
  PASS; TodoX.Web.dll verified. Artifacts excluded from commit.

No database update or migration required. No production deployment performed.
IMAGE/VIDEO generation, TTS/audio, billing, scene history and final merge were
not edited. Pre-existing user changes remain untouched. Gommo HTTP payload and
streaming handling remain unchanged. Live provider repair was not exercised;
tests use scripted provider responses and the existing Gommo streaming tests.
