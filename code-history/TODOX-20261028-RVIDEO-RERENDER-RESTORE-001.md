# RVideo explicit scene video rerender restore

- CHANGE_ID: TODOX-20261028-RVIDEO-RERENDER-RESTORE-001
- FEATURE_ID: RVID-UI-V2-SCENE-RERENDER-HISTORY
- Date: 2026-10-28
- Original branch: fix/rvideo-final-merge-recompose
- Delivery branch: fix/rvideo-rerender-restore-20261028
- Commit SHA: see Git commit containing this history entry.

## Evidence and scope

The observed scene-card layout matches the legacy `RenderVideoJobs.razor` markup. Its scene menu contained only image actions; `RerenderSceneVideoAsync` shared `CanCreateSceneVideo`, which explicitly rejected completed output. The V2 action and `SceneVideoRenderHandler.EnqueueSceneChildJobAsync` also rejected `AlreadyCompleted`. Thus a button-only fix would still be skipped by the worker.

V2 persisted a composite model option key but passed it as `RequestedModelCode`, dropping mode. Its request also omitted the latest version/job snapshot required by the existing atomic stale-attempt guard. Its dirty-only UI ignored duration/model-only rerenders. V2 video requests fell back to fixed aspect/resolution instead of using the existing project render-settings resolver.

Two new structural regression tests failed before production changes. Runtime policy tests now cover completed-output intent, active/invalid input rejection, unresolved submission, composite-key mapping and fresh logical-request identity. No claim is made about the live state of production job `2f70f08a-631b-4f50-ba34-3717e84c74ca`; no authenticated production UI or database investigation was performed. The suggested `RVideoSupersedeButton.razor` and original `SceneVideoSupersedeUiTests.cs` were absent; this change adds tests in the existing sibling test suite.

## Changed files and behavior

1. `TodoX.Web/Components/Pages/RenderVideoJobs.razor`: add explicit new-video, edit-video-prompt and video-history menu entries; permit completed output only for explicit rerender; save draft before confirmation; honor selected overrides and use existing guarded enqueue for new attempts. Initial/bulk create eligibility stays unchanged.
2. `TodoX.Web/Components/Pages/RVideo/RVideoV2Preview.razor`: explicit rerender with unchanged prompt/settings allowed; persist duration consistently on model switch; include current attempt snapshot and project aspect/resolution in video request.
3. `TodoX.Web/Services/VideoRender/RVideoSceneVideoRenderAction.cs`: accept completed eligibility for explicit rerender and map persisted option keys into canonical model/mode/duration.
4. `TodoX.Web/Services/VideoRender/SceneVideoRenderHandler.cs`: allow explicit rerender past completed-output eligibility; reuse existing child-job/version pipeline.
5. `TodoX.Web/Services/VideoRender/VideoRenderEligibilityService.cs`: active work takes precedence over old completed output; pending reconciliation remains blocking without a task ID.
6. `TodoX.Web/Services/VideoRender/RVideoRerenderRequestPolicy.cs`: internal policy shared by submission boundaries and saved override mapping. Worker catalog validation remains authoritative.
7. `TodoX.Web.Tests/SceneVideoSupersedeUiTests.cs`: regression tests.
8. This per-change record.
9. `code-history/features/RVID-UI-V2-SCENE-RERENDER-HISTORY.md`: feature lineage update.

Reuse: menu/button -> existing draft-save and confirmation -> guarded render job -> existing batch handler -> scene worker -> existing versioning INSERT with incremented version number and fresh logical request. Queuing does not delete or deselect the old version; existing completion/selection behavior remains authoritative. Repeated manual generation is deliberately supported even when the prompt is unchanged.

## Validation (commands executed from TodoX.Web)

- Before fix: `dotnet test ../TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release --filter FullyQualifiedName~SceneVideoSupersedeUiTests --nologo -v quiet`: 2 FAIL, reproducing missing UI permission and downstream rerender acceptance.
- `dotnet test ../TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release --filter 'FullyQualifiedName~SceneVideoSupersedeUiTests|FullyQualifiedName~RVideoP3B|FullyQualifiedName~RVideoRerenderHardeningTests' --nologo --logger 'trx;LogFileName=rerender-focused.trx' -v minimal`: 50 PASS, 0 FAIL; includes P3B1/P3B2.
- `dotnet test ../TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release --no-build --nologo --logger 'trx;LogFileName=rerender-full-final.trx' -v minimal`: 1362 PASS, 25 FAIL, 25 SKIP. All 25 failures also fail on the unmodified baseline; zero newly failing test names. These are not a green full suite.
- Baseline: detached worktree at `d26a49e`, copied the locally ignored test project file to run the same test harness; `dotnet test ../artifacts/rvideo-rerender-baseline/TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release --nologo --logger 'trx;LogFileName=baseline.trx' -v minimal`: 1346 PASS, 26 FAIL, 25 SKIP. One extra baseline-only layout failure; ignored local tests explain additional passing tests in the main workspace. Baseline source was not edited.
- `dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --nologo --filter 'FullyQualifiedName~RVideoVideoHotfixTests|FullyQualifiedName~SceneMediaVersion|FullyQualifiedName~RVideoFoundationTests' --logger 'trx;LogFileName=rerender-phase1.trx' -v minimal`: compile blocked by existing CS7036 at `RVideoVideoHotfixTests.cs:2852`, missing `reconciliationConfiguration`. Repeated on baseline Phase1B project with the same CS7036. Unrelated tests/production constructors were not changed.
- `dotnet format TodoX.Web.csproj whitespace --verify-no-changes --no-restore --include Services/VideoRender/RVideoRerenderRequestPolicy.cs Services/VideoRender/RVideoSceneVideoRenderAction.cs Services/VideoRender/VideoRenderEligibilityService.cs Services/VideoRender/SceneVideoRenderHandler.cs`: PASS. Build also runs existing compiler/MudBlazor analyzers.
- `git diff --check`: PASS. UTF-8 comparison scan found no additional replacement/mojibake markers.
- `dotnet build TodoX.Web.csproj -c Release --nologo -v minimal`: PASS, 0 errors, 46 existing warnings.
- `dotnet publish TodoX.Web.csproj -c Release -o ../artifacts/publish/todox-dashboard --nologo -v minimal`: PASS. Verified `TodoX.Web.dll` and IIS `web.config` exist.

## Package and limitations

Publish directory: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard` (explicit user AGENTS.md overrides skill publish defaults). Logs and baseline worktree are under ignored `artifacts`; no deploy output is committed. No IIS copy, restart or production deployment performed. Package creation is verified; live UI and a real provider render remain unverified. Full-suite baseline failures and Phase1B compile failure remain open validation limitations.

No database update, migrations or direct SQL execution. No changes to image generation, voice/audio, point/billing rules, provider payloads/catalog/models/endpoints or final merge. No YEScale provider work or metadata changes; YEScale MCP not called. No external API calls were needed for local policy/source tests.

Git: only the nine listed files are staged. Existing uploads, diagnostics, helper scripts, ignored test project and local tests remain untouched. Feature branch is pushed to origin; no main merge, force push or production deployment. Remote SHA and push outcome are reported in the final response.
