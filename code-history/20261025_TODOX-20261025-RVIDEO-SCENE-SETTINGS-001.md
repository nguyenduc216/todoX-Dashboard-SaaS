# RVideo scene menu and confirmed settings snapshot

FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER
CHANGE_ID: TODOX-20261025-RVIDEO-SCENE-SETTINGS-001
Date: 2026-10-25. Agent: Codex.

Baseline: origin/main 02ca91dde2aad25e43ae6ca6b1f4b503b66c4f18. Isolated feature/TODOX-20261025-RVIDEO-SCENE-SETTINGS-001 worktree; original fix/prompt-assistant-tts-validation and all its dirty/untracked files preserved.

Root cause: model/duration handlers marked override touched but never IsDirty. Preparation skipped save; ReloadAsync replaced these clean-looking drafts. Persisted explicit choices also were not considered intentional by the legacy enqueue path. Explicit Supersede inherited unused fields from the old input and the worker could fall back from the chosen model.

Changes: scene menu groups image/video/scene actions, hidden shared Supersede trigger retains consent/acknowledgement/idempotency/polling, responsive aligned selectors and pending-settings caption. Dropdown edits are dirty, saved before consent, preserved through reload, and selected duration is no longer silently replaced. Invalid combinations block Supersede. Preparation persists displayed selection/aspect/resolution. Request scene is deep-copied before confirmation; backend rejects concurrent saved prompt/duration/metadata edits. New input reuses only provider routing infrastructure; canonical service rebuilds prompt/model/mode/duration/source image/billing and draft service creates fresh generation/job/version/operation ownership. StrictRequestedSettings limits only these new explicit operations to their exact model/mode/duration/resolution. Old attempts/retry/polling/provider task fencing/image atomic enqueue/final merge remain unchanged. No provider network call, production SQL, IIS action or migration.

Files: RenderVideoJobs.razor; RVideo/RVideoSupersedeButton.razor; Services/VideoRender/RVideoSceneVideoRenderAction.cs; Services/VideoRender/SceneVideoWorkerHandler.cs; Services/VideoRender/SceneVideoOperationDraftService.cs; Tests/SceneVideoSupersedeUiTests.cs; this history; features/TODOX-RVIDEO-VIDEO-RERENDER.md.

Validation: dotnet build TodoX.Web/TodoX.Web.csproj -c Release --nologo PASS (0 errors; existing warnings). dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --filter "FullyQualifiedName~SceneVideoSupersedeUiTests|FullyQualifiedName~ResolveFallbackCandidates" --nologo: 26 PASS, 0 failed/skipped. Broader two-class check: 152 PASS / 6 FAIL; the same six historical source-text assertions fail on clean baseline (137 PASS / 6 FAIL). No full suite. git diff --check and UTF-8 validation PASS. Live provider/IIS acceptance left to operator.

Publish: designated existing D:/todoX/Dashboard-web/TodoXPortal/deployed-todoX-Dashboard-SaaS; preserve appsettings*.json, web.config and uploads using publish-item exclusions; verify hashes and DLL. Publish PASS; published DLL matches Release output SHA256; appsettings*.json and web.config SHA256 unchanged. Remote commit verification is reported by the executing agent.

Git: user explicitly authorizes commit/push origin/main. GitHub main protected=false, branch rules empty (verified via authenticated API); main remote unchanged before integration. Stage only the eight scoped files. No unrelated source, production configuration or deploy output included.
Commit SHA: see Git commit containing this history entry.
