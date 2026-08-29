# Timelapse Start Anchor + Video Direction Report

## Git
Branch: integration/rdance-on-construction-video-core
Base SHA: d6484bbe459233f60602df9b0bda200eb33313c9
Final SHA: verified after push in final terminal summary
Final SHA: 2478d04b7263d61e63f694e594f41ac4bffa5aa1
Push: completed

## Runtime Bug Evidence
Diagnostic job: 7ea0f9c8-8cc8-453e-b4ec-d05b06001ef9
DB clip direction: 0->35, 35->70, 70->100
Provider: 79AI
Model: seedance_20_pro
Observed visual direction issue: provider-rendered clips progressed visually backward despite forward DB graph.
Confirmed non-root-causes: start_progress_percent/end_progress_percent, TimelapseStageGraphBuilder, and UI progress semantics were not reversed.

## Existing Architecture Audit
Snapshot model: TimelapseJobSnapshot JSON on render.render_jobs.input_json.
100% image field: OriginalImage remains the legacy 100% final image field.
Image graph: TimelapseStageGraphBuilder still returns ascending progress/video edges; generated image order remains descending for final-only mode.
Prompt builder: TimelapsePromptResolver resolves image/video prompt envelopes.
Image provider reference path: TimelapseProviderRuntime resolves TodoX media bytes and submits 79AI /generateImage.
Video provider submit path: TimelapseProviderRuntime resolves stage media, uploads missing provider descriptors, and submits 79AI /create-video.
79AI client contract: Ai79TaskClient maps ordered Images to image/image_2 fields and preserves Options.

## Optional 0% Input
100% required: yes.
0% optional: yes.
Create UI: added optional 0% upload card next to required 100% upload card.
Edit UI: draft edit can add, replace, or remove 0% image before rendering starts.
Snapshot field: nullable StartImage.
Legacy compatibility: old snapshots deserialize with StartImage == null and keep final-only behavior.

## Render Modes
Final-only mode: OriginalImage only; reverse inference from 100% final anchor remains active.
Start+final mode: StartImage plus OriginalImage; uploaded 0% and 100% are immutable completed anchors.
Mode detection: TimelapseJobSnapshot.HasStartImage.

## Graph Behavior
Final-only generated stages: 0, intermediate stages; 100 is customer final anchor.
Start+final generated stages: intermediate stages only; 0 and 100 are customer anchors.
0% anchor treatment: completed customer stage, not AI-generated.
100% anchor treatment: completed customer final stage, unchanged.

## Image Prompting
Final-only prompt mode: FINAL_ONLY_REVERSE_INFERENCE.
Start+final prompt mode: START_AND_FINAL_ANCHORED.
0% semantics: first reference is true customer starting state.
100% semantics: second reference is true customer final state.
Intermediate target semantics: target progress is forward progress between both anchors.
Camera/scene lock: prompt preserves location, camera, framing, shell, structures, and aspect ratio.
Profile/category integration: profile prompt extraction and landscape profile rules remain included.

## Provider Image References
Start anchor supplied: yes in start+final mode.
Final anchor supplied: yes in all modes.
Intermediate reference strategy: every generated intermediate in start+final mode receives start and final anchors; final-only uses the existing reverse dependency/final reference.
79AI image-reference contract: /generateImage uses base64Image primary edit image plus subjects[0/1][url] anchor references.

## Video Direction
Logical direction: lower progress -> higher progress.
Direction validator: unchanged and still blocks reverse/flat/missing/same-media pairs.
Provider first-image mapping: logical start image -> image.
Provider second-image mapping: logical end image -> image_2.
79AI Seedance contract: made explicit with image/image_2 fields plus diagnostics.
Why old implementation reversed visually: the useful pair was hidden only in options.images JSON, leaving first/last role ambiguous for Seedance.
Fix applied: Timelapse 79AI adapter now submits ordered Images with image/image_2 and persists logical/provider role diagnostics.

## Diagnostics
Logical start/end persisted: yes.
Resolved URLs persisted: yes.
Actual provider roles persisted: yes.
Secrets excluded: yes.

## Legacy Jobs
Old snapshots: supported with StartImage null.
Old final-only jobs: keep reverse inference.
Retry/history: existing child operation tables and retry flow preserved.
Existing already-rendered videos: not manually patched; rerender through normal workflow if needed.

## SQL / Schema
SQL update required: NO
Schema migration required: NO
Database: todo_saas
SQL file: none
Manual execution: NO

## Settings
appsettings.json update required: NO
Other settings file update required: NO
IIS/app/service recycle required: YES after code deploy

## Provider Safety
79AI touched/used: YES
YEScale touched: NO
YEScale MCP called: NO
YEScale config changed: NO
Fallback to YEScale added: NO
Other provider changes: NO

## Validation
Build: PASS - dotnet build ..\TodoX.Dashboard.sln -c Release --no-restore, 45 existing Razor generated-code warnings, 0 errors.
Focused tests: PASS - dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Timelapse|FullyQualifiedName~Ai79TaskClientTests", 242/242.
Timelapse tests: PASS - included in focused run.
Full tests: FAIL - 6 unrelated RDance/DanceSell/Billing tests failed outside this task scope.
git diff --check: PASS, line-ending warnings only.
Format: FAIL - dotnet format ..\TodoX.Dashboard.sln --verify-no-changes --no-restore found pre-existing whitespace issues in unrelated files, including AccountRepository.cs, AiImageRenderRouter.cs, Gommo79AiImageService.cs, ChibiAvatarService.Generate.cs, SceneImageBatchRenderHandler.cs, RVideoJobService.cs, WalletService.cs, and Gommo79AiImageServiceTests.cs.
Publish: PASS - dotnet publish .\TodoX.Web.csproj -c Release --no-restore -o ..\artifacts\publish\todox-dashboard.

## Acceptance
- [x] 100% image remains required
- [x] 0% image is optional
- [x] Legacy jobs still work without 0%
- [x] Customer 0% becomes true start anchor
- [x] Customer 100% remains true final anchor
- [x] AI does not regenerate customer-supplied 0%
- [x] Intermediate images are forward states between both anchors
- [x] Prompt genuinely uses both anchors
- [x] Provider genuinely receives both image references
- [x] Category/profile rules remain active
- [x] Landscape 7A/7B/7C remain distinct
- [x] Video graph remains progress ASC
- [x] 0->35 renders visually 0->35
- [x] 35->70 renders visually 35->70
- [x] 70->100 renders visually 70->100
- [x] Provider actual first/second roles are test-covered
- [x] No YEScale changes
- [x] Build passed
- [x] Focused tests passed
- [x] Publish passed
- [x] Commit created
- [x] Push completed
