# RDance UI + Download Fix Report

## Git
Branch: integration/rdance-on-construction-video-core
Base SHA: 23124d4cc4946df545326895857a6e2bec3d9927
Final SHA: the pushed HEAD is reported in the task result (the report is committed within that self-referential commit).
Push: origin/integration/rdance-on-construction-video-core

## Information Tab
Old layout: nested md=8 information grid plus a standalone md=4 output panel.
New layout: two primary cards, each xs=12 md=6.
Standalone output panel removed: YES
Continue button removed: YES
Desktop 50/50: YES
Mobile stacking: YES

## Output Metadata
Service moved: YES, to the project card below a MudDivider.
Ratio moved: YES, to the project card below a MudDivider.
Format moved: YES, to the project card below a MudDivider.

## Download Root Cause
Current UI behavior: the result and reference buttons authorize the current job in the Blazor circuit and then download its customer media URL through todoxDownload.downloadRemoteFile.
Current endpoint: /api/dance-sell/jobs/{id}/download and /api/dance-sell/jobs/{id}/reference/download.
Authentication root cause: fresh top-level requests do not have the circuit-scoped AuthStateService.CurrentUser used by these endpoints, producing DANCE_SELL_UNAUTHORIZED even for an active Blazor user.
Response-wrapper issue present: YES. The prior generic ExecuteAsync<T> JSON-wrapped an IResult returned by download handlers.
Exact technical explanation: browser navigation to the API endpoint created a request outside the authenticated Blazor circuit; successful IResult values would also have been serialized as JSON through the generic response wrapper. The UI now performs the owned-job check in the active circuit and starts a Blob download without navigation. The endpoint handlers now use ExecuteResultAsync and return raw streaming IResult responses when called from a valid authenticated context.

## Download Implementation
Pattern reused from: existing todoxDownload browser Blob-download helper.
Authorization mechanism: DanceSell.GetAsync(jobId, AuthState.CurrentUser) before exposing the already stored customer media URL to JavaScript.
Ownership validation: IDanceSellPhase2Service.GetAsync enforces the existing owned-job check.
Streaming mechanism: endpoint proxy uses HttpCompletionOption.ResponseHeadersRead and copies upstream content directly to the HTTP response body.
Result filename: todox-rdance-{jobId}.mp4
Large-file safe: YES. No base64 conversion or Blazor JS payload is used. The browser download uses a Blob; the endpoint streams upstream content without server-side byte[] buffering.

## Result Tab
Preview: preserved.
Download button: calls DownloadResultAsync and stays on the RDance page.
Loading state: button is disabled and shows a compact preparing label.
Error handling: Snackbar messages cover session expiry and generic download failure without displaying raw exceptions.

## Reference Download
Same issue present: YES.
Fixed: YES.
Method: circuit ownership check plus direct customer media Blob download; the protected endpoint also returns a raw streaming response.

## Security
Unauthenticated blocked: YES, in both circuit service access and protected endpoint helper.
Wrong tenant blocked: YES, by DanceSell.GetAsync ownership validation.
Owner allowed: YES, subject to stored customer media URL accessibility.
Provider secrets exposed: NO.

## Database / Settings
SQL update required: NO
Schema migration required: NO
appsettings.json update required: NO
Other settings update required: NO
IIS/app recycle required: NO, unless the normal deployment process requires application reload to load the new binaries.

## Provider Safety
79AI provider config changed: NO
YEScale touched: NO
YEScale MCP called: NO
YEScale config changed: NO
YEScale fallback added: NO
Other provider changes: NO

## Validation
Build: dotnet build ..\TodoX.Dashboard.sln -c Release --no-restore - passed with 0 warnings and 0 errors.
Tests: dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~RDanceFashionDemoPageTests - passed, 27 passed.
git diff --check: passed.
Format: dotnet format ..\TodoX.Dashboard.sln whitespace --verify-no-changes --no-restore --include Components\Pages\RDanceJobDetail.razor Services\DanceSell\DanceSellPhase2Endpoints.cs wwwroot\js\todox-download.js ..\TodoX.Web.Tests\RDanceFashionDemoPageTests.cs - passed.
Publish: dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o ..\artifacts\publish\todox-dashboard - passed.
Publish output: D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard

## Acceptance
- [x] Standalone output card removed
- [x] Continue button removed
- [x] Project card 50%
- [x] Motion card 50%
- [x] Mobile cards stack
- [x] Service/ratio/format shown in project card
- [x] Result video preview works
- [x] Download no longer opens JSON unauthorized page
- [x] Owner can start MP4 download
- [x] Wrong tenant cannot pass owned-job validation
- [x] Unauthenticated request remains protected
- [x] Successful endpoint response is video/mp4, not JSON
- [x] Endpoint large-file response is streamed, not base64 buffered
- [x] No SQL migration
- [x] No appsettings update
- [x] Build passed
- [x] Tests passed
- [x] Publish passed
- [x] Push target prepared
