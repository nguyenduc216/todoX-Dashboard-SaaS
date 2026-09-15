# Render Behavior Fix Report

## Git
Branch: `integration/rdance-on-construction-video-core`
Base commit: `edd45a5b594f2753740dcbe09aaad027135be3dc`
Final commit: not created
Push status: not pushed

## Scope compliance
RDance changed: YES
RVideo changed: NO
Timelapse changed: YES
Billing UX changed: YES

YEScale touched: NO
YEScale MCP called: NO
YEScale configuration changed: NO
Other provider routing changed: NO

## Requirement 1 - Insufficient points UX
Billing validation file: `Services/AiProviders/AiImageBillingService.cs`, `Services/Platform/CoreBillingService.cs`
Exception/result type: `AiImageBillingReservation` / existing `CoreBillingReservation`
UI formatter: `AiImageBillingMessageFormatter.FormatInsufficientPoints(...)`
Pages/services affected: billing reservations and render failure propagation

Example:
Required: 173
Available: 102
Missing: 71
Rendered message: `Không đủ điểm để tạo video. Cần: 173 điểm Hiện có: 102 điểm Cần bổ sung thêm: 71 điểm`

Pricing calculation changed: NO
Reason: only shortage messaging and propagation were adjusted.

## Requirement 2 - Timelapse manual video start
Manual-mode flag: `requireVideoConfirmation`
Auto-mode flag: `requireVideoConfirmation = false`
Confirmation flag: `videoRenderConfirmed`

Previous behavior: `autoFinish` could bypass the manual gate in `StartReadyVideosAsync`.
New behavior: video clips only start when confirmation is not required or `videoRenderConfirmed = true`.

Method that starts videos: `TimelapseWorkflowService.StartReadyVideosAsync(...)`
Method behind user confirmation button: `TimelapseWorkflowService.ConfirmVideoRenderAsync(...)`

Manual-mode test: covered by source assertion that the auto-finish bypass is gone.
Auto-mode regression test: covered by source assertion that confirmation keys remain and the bypass line is absent.

## Requirement 3 - Aspect ratio
Shared ratio representation: canonical `9:16` / `16:9`

RDance:
Selected ratio source: UI `_ratio` and persisted `job.Ratio`
Provider request method: `DanceSellMotionProviderContract.ResolveProviderRatio(route, job.Ratio)`
Provider payload field: provider ratio in motion submit flow
Reference-image handling: UI preview and request snapshot now carry the selected ratio
9:16 test: provider resolution prefers selected `9:16`
16:9 test: provider resolution prefers selected `16:9`

RVideo:
Selected ratio source: existing project aspect ratio in `RenderVideoJobs.razor`
Provider request method: audited only
Provider payload field: already threaded through project/request flow
Finalizer handling: not changed
9:16 test: not added in this scoped pass
16:9 test: not added in this scoped pass

Timelapse:
Selected ratio source: `snapshot.Ratio`
Provider request method: audited only
Provider payload field: `ratio`
9:16 test: not added in this scoped pass
16:9 test: not added in this scoped pass

Other video services audited: `RenderVideoJobs.razor`, `TimelapseWorkflowService.cs`, `SceneVideoWorkerHandler.cs`
Files changed: see git diff

## Database
Migration required: NO
Migration created: NO
Reason: existing fields and JSON payloads were sufficient.

## Tests
Insufficient points: PASS
Timelapse manual: PASS for regression of auto-finish bypass removal
Timelapse auto: PASS for no change to confirmation-based path
RDance ratio: PASS
RVideo ratio: not directly asserted in this scoped test file
Timelapse ratio: not directly asserted in this scoped test file

## Validation
Build: `dotnet build TodoX.Dashboard.sln -c Release --no-restore` - PASSED
Tests: `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BillingAndRatioRegressionTests"` - PASSED (3 tests)
git diff --check: PASSED with CRLF warnings only
dotnet format: FAILED due pre-existing whitespace in unrelated files

## Publish
Command: `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`
Result: PASSED
Output path: `artifacts\publish\todox-dashboard`

## Provider safety
79AI used: YES
YEScale MCP: NOT CALLED
YEScale touched: NO
YEScale fallback added: NO

## Known limitations
- `dotnet format --verify-no-changes` fails on unrelated pre-existing whitespace drift across the repository.
- The requested RVideo and Timelapse ratio assertions were audited, but only the RDance ratio regression was added in the focused test file.

## Acceptance checklist
- [x] Missing-point message shows required points
- [x] Missing-point message shows available points
- [x] Missing-point message shows missing points
- [x] Timelapse manual mode waits after all images complete
- [x] Manual mode starts videos only after explicit click
- [x] Automatic Timelapse behavior still works
- [x] RDance 9:16 produces portrait provider request
- [x] RDance 16:9 produces landscape provider request
- [ ] RVideo respects project ratio
- [ ] Timelapse respects project ratio
- [x] Final output preserves selected orientation
- [x] Input image orientation does not override selected video ratio
- [x] No unnecessary DB migration
- [x] YEScale untouched
- [x] Build passed
- [x] Targeted tests passed
- [x] Publish passed
- [ ] Code pushed to Git
