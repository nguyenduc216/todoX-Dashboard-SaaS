# RVideo bootstrap skeleton and MudTabs visibility correction

FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER
CHANGE_ID: TODOX-20261025-RVIDEO-TAB-BOOTSTRAP-005
Date: 2026-10-25. Agent: Codex.
Baseline: origin/main 4873de2e28ca31d9ef0cc30fa58cdf035d53fbfc. Isolated feature/rvideo-tab-bootstrap-005 worktree wt-rvideo-tab-bootstrap. Original user workspace preserved.

Verified root cause tab/content desync: installed MudBlazor 9.6 stylesheet explicitly sets .mud-tabs-panels>.mud-tab-panel display:none and only .mud-tab-panel-active:not(.mud-tab-panel-hidden) display:contents. Our scoped CSS previously forced every panel div and panelClass to display:flex with greater specificity. With KeepPanelsAlive=true all inactive content became visible. Fix limits flex layout to the official active class only, preserving mounted inactive players and MudTabs' single active index. No DOM class manipulation or JS activation override.

Black/empty load: RVideoJobDetail auth/job waiting states had only a progress bar, and non-streamed async SSR could withhold content while loading. Adds streaming render and a substantial header/tab/content skeleton, spinner/loading text, 5-second slow-loading notice, bounded 25-second auth wait with retry, and 15-second task WaitAsync in addition to service cancellation. Late old-job results/errors are ignored and navigation reload resumes after the old request releases its guard. Streaming enabled on embedded page as well. Error states retain retry rather than unlimited progress. No claim of full production browser reproduction.

Restore: one initialization per view key; user tab selection revision wins over stored state including clicks while module import/restore is in flight. After restoring tab, defer scroll until next panel render. JS waits two animation frames, restores only visible laid-out containers once, and retains clamping/session isolation/non-disruptive attach behavior. Existing incremental refresh, stable final src and explicit 'Xem video mới' stay unchanged.

Files: Components/Pages/RVideoJobDetail.razor; Components/Pages/RenderVideoJobs.razor; Components/Pages/RenderVideoJobs.razor.css; wwwroot/js/rvideo-view-state.js; Tests/rvideo-view-state.test.cjs; Tests/rvideo-tabs.browser.cjs; Tests/RVideoBootstrapBehaviorTests.cs; this history; features/TODOX-RVIDEO-VIDEO-RERENDER.md.

Validation: Release build 0 errors (existing warnings). dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --filter "FullyQualifiedName~RVideoBootstrapBehaviorTests|FullyQualifiedName~RVideoViewStateTests|FullyQualifiedName~RVideoLiveStatusAndRemergeTests": 16 PASS / 0 failed/skipped, including delayed loading then real service error/TimeoutException -> retry state. node TodoX.Web/Tests/rvideo-view-state.test.cjs PASS.

Real Chromium/Playwright local component harness uses MudBlazor 9.6, actual page CSS (scoped selector translated for harness), actual view-state JS and interactive-server MudTabs KeepPanelsAlive. UI interaction test Tests/rvideo-tabs.browser.cjs PASS: 0->1->2->0 highlight/content, exactly one visible panel, F5 restoring Preview with 700px div scroll and F5 Result tab. Harness lives under OS temp/rvideo-browser-check/harness, no application auth/database/provider. Full job-route auth/network/skeleton browser E2E, actual video playback/fullscreen/polling E2E and production acceptance remain NOT RUN; no conclusive end-to-end production fix claim. Slow/failure bootstrap uses functional component/service probes rather than source-text assertions. No full suite.

No render/Supersede/billing/provider/merge/schema changes; no SQL migration, production SQL, real provider call or IIS deployment. User authorizes commit/push origin/main then existing D:/todoX/Dashboard-web/TodoXPortal/deployed-todoX-Dashboard-SaaS overwrite with DLL/CSS/JS, preserving production configuration/uploads. Actual SHA and publish verification in final report.
Commit SHA: see Git commit containing this history entry.
