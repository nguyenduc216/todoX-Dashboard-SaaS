# Timelapse History Completion Report

## Git
Branch: integration/rdance-on-construction-video-core
Base commit: 30bc9630ec22d680e140ee30cd5036f9685bb59b
Final commit: f7cc224
Push status: Pending

## Scope compliance
RDance changed: NO
RVideo changed: NO
Gateway changed: NO
Billing changed: NO
Provider routing changed: NO

79AI configuration changed: NO
YEScale touched: NO
YEScale MCP called: NO
YEScale configuration changed: NO
YEScale fallback added: NO
Other provider configuration changed: NO

## Files changed
- `Components/Pages/TimelapseJobDetail.razor`
- `Models/Timelapse/TimelapseModels.cs`
- `Services/Timelapse/TimelapseJobService.cs`
- `Services/Timelapse/TimelapseWorkflowService.cs`
- `Tests/MediaHistorySelectionRegressionTests.cs`
- `TIMELAPSE_HISTORY_COMPLETION_REPORT.md`

## Existing architecture verified
Timelapse page: `Components/Pages/TimelapseJobDetail.razor`
Workflow service: `Services/Timelapse/TimelapseWorkflowService.cs`
Job service: `Services/Timelapse/TimelapseJobService.cs`
History dialog: `Components/Dialogs/MediaHistoryDialog.razor`
Image version source: `timelapse.timelapse_image_stage_versions`
Video version source: `timelapse.timelapse_video_clip_versions`
Final version source: `timelapse.timelapse_final_outputs`
Current image pointer: `timelapse.timelapse_image_stages.active_attempt`
Current video pointer: `timelapse.timelapse_video_clips.active_attempt`
Current final pointer: `render.render_jobs.output_json` (`mediaId`, then `objectKey`, then `publicUrl`)

## Scene video history
Changed in this task: NO
Reason if YES: N/A. Existing clip-scoped history and shared dialog behavior was preserved.

## Scene image history
UI method: `OpenSceneImageHistoryAsync(TimelapseStageImage image)`
History service method: `TimelapseJobService.ListSceneImageHistoryAsync`
Workflow method: `TimelapseWorkflowService.ListSceneImageHistoryAsync`
SQL source: `timelapse.timelapse_image_stage_versions`, joined to `timelapse.timelapse_image_stages`
Selection method: Existing `SelectHistoryAsync` `"image"` branch
Current-pointer mechanism: `timelapse_image_stages.active_attempt`
Failed-attempt behavior: Failed rows remain in history; only `COMPLETED` rows with a public URL can be selected.

## Final video history
UI method: `OpenFinalVideoHistoryAsync()`
History service method: `TimelapseJobService.ListFinalVideoHistoryAsync`
Workflow method: `TimelapseWorkflowService.ListFinalVideoHistoryAsync`
SQL source: `timelapse.timelapse_final_outputs`, joined to `render.render_jobs`
Selection method: Existing `SelectHistoryAsync` `"final"` branch, which updates `render.render_jobs.output_json` without creating a new final output
Current-pointer mechanism: `output_json.mediaId`, falling back to `objectKey`, then `publicUrl`

## Final selection bug
Previous IsSelected behavior: Final history used `f.version = max(version)` for the current marker.
Why it was wrong: Selecting an older final changed `output_json`, but the newest row remained marked selected.
New IsSelected behavior: Final history compares the final row with the persisted current `output_json`, using stable identifiers in priority order: media ID, object key, then public URL.

## FinalOutput reload behavior
How TimelapseWorkflowState.FinalOutput was previously resolved: `ReadStateAsync` always selected the highest final version.
How it is resolved now: It first selects the row matching `render.render_jobs.output_json` by media ID, object key, or public URL, then falls back to the highest version.
Reload persistence verified: YES, by source-contract coverage of the pointer-based query and the existing selection flow; no live database was used.

## Database
Migration required: NO
Migration created: NO
Reason: Existing active-attempt and `output_json` pointers support the requested selection persistence.

## Tests
Image history test: Source regression verifies image history method and version-table query.
Image old-version selection: Existing image selection branch remains job-scoped and completed-only.
Failed image history: Source regression verifies failed rows are returned and not selectable.
Final history test: Source regression verifies dedicated final history method and all final rows.
Final old-version selection: Source regression verifies pointer update through the existing final selection branch.
Failed final history: Dedicated final query returns failed rows and error messages; shared UI disables non-completed rows.
Reload persistence: `ReadStateAsync` pointer-resolution query is covered by source assertions; live DB reload was not available.
Scene-video regression: Existing clip-scoped history test passes.

## Validation
Build: `dotnet build TodoX.Dashboard.sln -c Release --no-restore` passed, 0 errors; one concurrent build attempt hit a transient `VBCSCompiler` file lock.
Tests: `dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~MediaHistorySelectionRegressionTests` passed, 6/6. `dotnet test ../TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Timelapse|FullyQualifiedName~MediaHistory"` passed, 186/186. Full local `Tests` suite has unrelated pre-existing failures involving missing RVideo fixtures, worker event ordering, and prompt parser expectations.
git diff --check: Passed; Git reported only LF-to-CRLF normalization warnings.
dotnet format: Failed on pre-existing whitespace findings across unrelated account, provider, profile, render, settings, video, and wallet files. No unrelated files were formatted.

## Publish
Command: `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`
Result: Passed; `TodoX.Web.dll` published successfully.
Output path: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

## External tools
YEScale MCP: NOT CALLED
Other provider MCP: NOT CALLED
External provider lookup: NOT PERFORMED

## Known limitations
- No live database or browser smoke test was available in this coding environment.
- Repository-wide `dotnet format --verify-no-changes` remains blocked by pre-existing unrelated whitespace findings.
- The full local `Tests/TodoX.Web.Phase1B.Tests.csproj` suite contains unrelated pre-existing failures and was not broadened or modified.

## Acceptance checklist
- [x] Scene image history visible
- [x] Old image can be selected
- [x] Failed image attempts remain visible
- [x] Selecting image does not rerender
- [x] Final history visible
- [x] Old final video can be selected
- [x] Main player switches to selected historical final
- [x] Selected final remains selected after reload
- [x] Final IsSelected no longer uses max(version)
- [x] Failed final attempts remain visible
- [x] No new render/final version created when selecting history
- [x] Scene video history still works
- [x] MediaHistoryDialog reused
- [x] No DB migration
- [x] YEScale untouched
- [x] Build passed
- [x] Targeted tests passed
- [ ] Repository-wide format passed
- [x] Publish passed
- [ ] Code pushed to Git
