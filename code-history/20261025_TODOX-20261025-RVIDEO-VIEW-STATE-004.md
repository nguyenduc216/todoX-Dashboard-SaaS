# RVideo incremental UI refresh and project-scoped view state

FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER
CHANGE_ID: TODOX-20261025-RVIDEO-VIEW-STATE-004
Date: 2026-10-25. Agent: Codex.
Baseline origin/main 46feda13ba80eec70b44ea0412c7ac0bc8feddf2. Isolated feature/TODOX-20261025-RVIDEO-VIEW-STATE-004 / wt-rvideo-view-state. Existing user workspace unchanged.

Verified source causes: periodic polling invoked full ReloadAsync, rehydrating prompt workspace, settings/defaults, character/model data, billing estimate and all histories. Parameter bootstrap compared resolved project identity against incoming unresolved parameters, enabling repeated bootstrap/reset to tab zero; full query assigned project directly including null results. Tabs did not retain inactive panels and no tab/div-scroll persistence existed. Actual production black-screen browser trace was not available, so no claim of a reproduced browser exception.

Changes (UI only): PollLoopAsync invokes RefreshLiveStateAsync under the existing nonblocking reload lock. Fetch project/status/events and jobs/operation states; mutate existing project/scene instances without changing prompt/draft/tab/dialog. Refresh media version lists/history when project/event revision changes and every third cycle as a missed-event recovery. Full ReloadAsync remains for open/explicit editor/structural operations; it no longer overwrites the last good project with null and rejects project navigation mismatches. Static catalogs/workspace/settings/estimate are excluded from periodic polling. Final history loads into local collections before committing. Existing one-loop/cancellation model retained.

Incoming parameter identity now tracked independently of the resolved project. Tab callback persists selection in sessionStorage under rvideo:view:<projectId> (job key fallback); restore after loaded project/dialog render, never on poll. KeepPanelsAlive and stable scene keys preserve DOM. Module captures real info/preview/result/dialog div scrollTop via passive lifecycle-independent scroll events, clamps restore, and restores each visible element once on open, not each poll. No window.scrollY or URL/back changes. Session storage contains only tab and scroll numbers, no content/credentials. Storage/interop failures preserve the view.

Result player retains its displayed final URL through incremental updates. New final version offers explicit 'Xem video mới'; user chooses when to switch source. Inactive panels retained, unchanged URL does not reload src. Scene operation statuses still use canonical UI states and media history. No backend/provider/Supersede/consent/pricing/fencing/image enqueue/recovery/final merge/selection behavior changed.

Files: TodoX.Web/Components/Pages/RenderVideoJobs.razor; TodoX.Web/wwwroot/js/rvideo-view-state.js; TodoX.Web/Tests/RVideoViewStateTests.cs; TodoX.Web/Tests/rvideo-view-state.test.cjs; this record; code-history/features/TODOX-RVIDEO-VIDEO-RERENDER.md.

Validation: node TodoX.Web/Tests/rvideo-view-state.test.cjs PASS (tab isolation, scroll capture/clamping and no forced scroll on repeated attach). dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --filter "FullyQualifiedName~RVideoViewStateTests|FullyQualifiedName~RVideoLiveStatusAndRemergeTests" --nologo: 14 passed, zero failed/skipped (identity/tab/prompt/player preservation, cross-project late response, live operation state and single poll loop). Release build zero errors with existing warnings. git diff --check/UTF-8 checks. Full suite and live browser/fullscreen/F5 acceptance not run; operator acceptance required. No production SQL/provider call/IIS action. No SQL migration.

User authorizes commit/push origin/main and overwrite existing D:/todoX/Dashboard-web/TodoXPortal/deployed-todoX-Dashboard-SaaS. Preserve appsettings*.json/web.config/uploads via publish exclusions; verify hashes and final commit metadata. Actual remote SHA and final publish verification are reported by executing agent.
Commit SHA: see Git commit containing this history entry.
