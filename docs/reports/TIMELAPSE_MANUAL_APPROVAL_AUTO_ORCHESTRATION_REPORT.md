# Timelapse Manual Approval / Auto Orchestration Report

## Git
Branch: integration/rdance-on-construction-video-core
Base SHA: 9baa4546039e2f5d5071a710d41b1417f6845918
Final SHA: 005f6894f76f24408e1b4d8544a48fbfa0618c2e (code fix commit; final pushed branch SHA is verified in the terminal summary)
Push: completed after report commit

## Audit
Current AutoFinish behavior: Auto mode stores RequireVideoConfirmation=false and allows StartReadyVideosAsync to start clips when endpoint images are complete.
Current RequireVideoConfirmation behavior: Manual mode stores RequireVideoConfirmation=true when AutoFinish is false.
Current VideoRenderConfirmed behavior: ConfirmVideoRenderAsync persists videoRenderConfirmed=true and then reuses StartReadyVideosAsync.
Current ReadyVideoCount behavior: Counts WAITING/INVALIDATED clips whose two endpoint image stages are COMPLETED.
Current CanConfirmVideoRender behavior: Previously became true when any ready clip existed; now requires all timeline images completed, no active image render, at least one video, and no prior confirmation.
Current StartReadyVideosAsync behavior: Preserved. It starts only clips with completed endpoint images and requires either requireVideoConfirmation=false or videoRenderConfirmed=true.

## Root Cause
Why manual approval became available too early: CanConfirmVideoRender used readyVideoCount > 0, so manual approval appeared as soon as one adjacent pair was ready instead of waiting for every image stage in the timeline.

## Final Rules
Auto mode: Any ready adjacent clip may start immediately; no all-image gate was added.
Manual mode: No video starts before approval; approval is allowed only after all image stages are COMPLETED and no image stage is rendering.

## Auto Mode Validation
Partial image set: 100 and 70 complete, 35 rendering, 0 waiting.
Ready adjacent pair: 70->100.
Clip started immediately: Covered by preserved StartReadyVideosAsync gate and TimelapseVideoOrchestration test.
Other clips waiting: 35->70 and 0->35 are not ready.

## Manual Mode Validation
Partial image set: 100 and 70 complete, 35 rendering, 0 waiting.
Approval enabled: false.
Any video started: StartReadyVideosAsync remains blocked until videoRenderConfirmed=true.
All image set completed: 0, 35, 70, 100 completed.
Approval enabled: true.
Button label: DUYỆT ẢNH & TẠO VIDEO.
After click: ConfirmVideoRenderAsync persists approval and calls StartReadyVideosAsync.
VideoRenderConfirmed: true after confirm.
Videos started: Ready clips start through existing orchestration.

## Optional 0% Image
Manual all-image rule: Includes every TimelapseStageImage, not only AI-generated images.
0% anchor behavior: Completed customer anchor counts as one completed timeline stage.
100% anchor behavior: Completed customer anchor counts as one completed timeline stage.

## UI
Manual waiting state: Shows "Đang chờ hoàn thành toàn bộ ảnh trước khi duyệt."
Manual ready state: Shows ready info text and enabled approval button.
Button label: DUYỆT ẢNH & TẠO VIDEO.
Auto approval UI hidden: CanConfirmVideoRender requires RequireVideoConfirmation=true.

## Retry/Rerender
Existing behavior: Image rerender invalidates affected downstream images, videos, and final output; video start still respects the manual confirmation SQL gate.
Approval reset needed: Not implemented in this scoped fix because existing code does not reset VideoRenderConfirmed on rerender and the requested root bug is early initial approval.
Implemented behavior: Preserved existing invalidation semantics and documented the residual product decision.

## SQL / Schema
SQL update required: NO
Schema migration required: NO
Database: todo_saas
SQL file: N/A

## Settings
appsettings.json update required: NO
Other settings file update required: NO
IIS/app/service restart or recycle required: YES after deployment

## Provider Safety
79AI touched/used: NO provider code changed
YEScale touched: NO
YEScale MCP called: NO
YEScale config changed: NO
Fallback to YEScale added: NO
Other provider changes: NO

## Validation
Build: PASS - dotnet build TodoX.Web/TodoX.Web.csproj -c Release --no-restore
Focused tests: PASS - dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TimelapseApprovalRegressionTests"; PASS - approval/UI/history focused filter excluding known unrelated worker assertion
Timelapse tests: PARTIAL - broader TimelapseWorkerClaimRegressionTests includes pre-existing brittle source-order assertion for TIMELAPSE_IMAGE_MODEL_SUBMITTED vs TIMELAPSE_IMAGE_MODEL_SUCCEEDED.
Full tests: NOT RUN due scoped validation and unrelated focused failure.
git diff --check: PASS with CRLF normalization warnings only.
Format: Scoped manual edits only; no mass format.
Publish: PASS - dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard

## Acceptance
- [x] Auto mode starts a clip immediately when its two endpoint images are complete
- [x] Auto mode does not wait for all images
- [x] Auto mode requires no manual approval
- [x] Manual mode starts no video before approval
- [x] Manual approval remains disabled until ALL image stages are completed
- [x] Manual approval button says "DUYỆT ẢNH & TẠO VIDEO"
- [x] Confirm action persists VideoRenderConfirmed=true
- [x] Confirm action starts all ready video clips
- [x] Server rejects manual approval if any image is incomplete
- [x] Optional 0% anchor works correctly
- [x] Existing video direction fix remains intact
- [x] No YEScale changes
- [x] Build passed
- [x] Focused tests passed
- [x] Publish passed
- [x] Commit created
- [x] Push completed
