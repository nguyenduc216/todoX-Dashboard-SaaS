# RVID-RESOLUTION-UI-SYNC.1

Source evidence: CheckPromptAsync reapplied prompt settings to the dropdown.
LoadPromptWorkspaceAsync replaced the project prompt with active generation JSON
and reapplied those settings. Reload did not load resolution from the core job
snapshot. Thus an old generation could override a successfully saved selection.
No database persistence failure was reproduced or asserted.

Changes (only RenderVideoJobs.razor):
- Track dirty resolution separately from persisted resolution and project identity.
- Reload reads core input resolution, falling back to canonical project prompt.
- ApplyVideoSettingsFromPrompt retains dirty selection, then persisted settings,
  before using generation prompt settings.
- CheckPromptAsync updates diagnostics without applying video settings.
- SynchronizeResolutionDiagnostics parses an in-memory resolution override;
  raw prompt remains unchanged until Save. Other schema errors remain visible.
- PersistCurrentJobAsync normalizes selection, updates canonical prompt and
  diagnostics, and reads back core snapshot before reporting Save success.
- New scene render inputs use persisted resolution. No enqueue on dropdown change.
- New project resets resolution tracking.

Changed files:
- TodoX.Web/Components/Pages/RenderVideoJobs.razor
- TodoX.Web.Tests/RVideoResolutionUiSyncTests.cs
- This history file.

Commands (from TodoX.Web unless stated):
- dotnet test ../TodoX.Web.Tests/TodoX.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~RVideoResolutionUiSyncTests|FullyQualifiedName~TodoXVideoPromptParserTests|FullyQualifiedName~RVideoJobUpdateHelperTests" --verbosity quiet
  PASS: 22 tests. Tests exercise component settings logic by reflection, prompt
  canonicalization, stale/invalid diagnostics and source boundaries for check,
  dropdown change and render input. No live database or browser integration run.
- dotnet format whitespace TodoX.Web.csproj --no-restore --verify-no-changes --include Components/Pages/RenderVideoJobs.razor
  PASS.
- dotnet format whitespace ../TodoX.Web.Tests/TodoX.Web.Tests.csproj --no-restore --verify-no-changes --include ../TodoX.Web.Tests/RVideoResolutionUiSyncTests.cs
  PASS.
- git diff --check: PASS. Changed page diff contains no new mojibake strings.
- Repository root: dotnet build TodoX.Web/TodoX.Web.csproj -c Release --verbosity quiet
  PASS: 0 errors, 46 existing warnings.
- Repository root: dotnet publish TodoX.Web/TodoX.Web.csproj -c Release -o artifacts/publish --verbosity quiet
  PASS. Local artifact only, excluded from Git.

Remaining verification: real Save/Reload for project 175 and browser interaction.
Reload adds a core job read; persistence keeps the existing multi-write workflow.
No provider submit/polling, reconciliation, billing/refund, TTS, scene history,
render engine, merge or database schema changed. Existing user changes retained.
No migrations and no production deployment.
