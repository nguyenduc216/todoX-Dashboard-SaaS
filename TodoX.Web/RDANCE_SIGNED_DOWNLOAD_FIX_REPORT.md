# RDance Signed Download Fix Report

## Git
Branch: `integration/rdance-on-construction-video-core`
Base SHA: `f857fd51d066996d32ff0bad97c043ccc6f415f1`
Final SHA: not committed
Push: not performed

## Runtime bug
Result CDN: `https://ai-cdn.gommo.net/ai/videos/...mp4`
Cross-origin: yes, from `dashboard.todox.vn` to `ai-cdn.gommo.net`
Old Blob flow: browser `fetch` followed by `response.blob()` and anchor download
Observed symptom: download button remained in preparation state without starting a reliable download

## Ticket architecture
Implementation: `IRDanceDownloadTicketService` with ASP.NET Core Data Protection
Protection mechanism: time-limited Data Protection protector, purpose `TodoX.RDance.DownloadTicket.v1`
TTL: 3 minutes
Bound fields: job ID, nullable customer ID, user ID, download type, issue time, expiry
One-time: no; short-lived protected ticket is sufficient
DB persistence needed: no

## Authorization
Circuit ownership check: ticket creation calls the existing `RequireOwnedJobAsync` path
Wrong tenant blocked: yes, before ticket issue through existing ownership rules
Unauthenticated blocked: yes, before ticket issue through `ExecuteAsync`

## Endpoint
Result route: `GET /api/dance-sell/jobs/{id}/download?t={ticket}`
Reference route: `GET /api/dance-sell/jobs/{id}/reference/download?t={ticket}`
Ticket validation: protected ticket, expiry, route job ID, and result/reference type
URL resolution: repository loads the current owned job record and resolves its stored result/reference URL
SSRF protection: HTTPS plus DNS/IP checks reject localhost, loopback, private, link-local, and local IPv6 addresses

## Streaming
ResponseHeadersRead: yes
Whole-file buffering: no; upstream response stream is copied to the HTTP response body
Content-Type: `video/mp4` for result; upstream image media type or `image/jpeg` fallback for reference
Content-Disposition: attachment with job-bound filename
Browser navigation behavior: hidden same-origin iframe starts the attachment download while keeping the page open

## JS
Old fetch/blob used for RDance: NO
New helper: `todoxDownload.startBrowserDownload`
Page stays open: yes

## Data Protection
Existing configuration: no explicit `AddDataProtection` configuration found
New configuration: none added
Persistent key ring: not explicitly configured
Single-server: tickets work during the current process; tickets are invalidated after an app restart with ephemeral/default keys
Multi-instance: not safe without shared persistent Data Protection keys

## Database / Settings
SQL update required: NO
Schema migration: NO
appsettings.json update: NO
Other settings: NO
Data Protection server configuration: NO for this code change; shared persistent keys are required separately for multi-instance continuity
IIS/app recycle: not required

## Provider safety
79AI provider config changed: NO
YEScale touched: NO
YEScale MCP called: NO
YEScale config changed: NO
Fallback added: NO

## Validation
Build: PASS, `dotnet build TodoX.Dashboard.sln -c Release --no-restore`
Targeted tests: PASS, 5/5 RDance signed-download tests
Full tests: FAIL, 7 unrelated pre-existing failures out of 202 tests
git diff --check: PASS, only line-ending warnings
format: FAIL, repository-wide pre-existing whitespace diagnostics outside this change
publish: PASS, `dotnet publish TodoX.Web.csproj -c Release -o D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

## Acceptance
- [x] Download click starts browser download promptly
- [x] No CDN fetch/blob in browser for result
- [x] No cross-origin CORS dependency
- [x] No full MP4 buffered in browser JS
- [x] Owner-only ticket issuance
- [x] Expired/tampered ticket rejected
- [x] Job-bound ticket
- [x] Result/reference separated
- [x] Backend streams CDN content
- [x] UI stays on RDance page
- [x] No SQL migration
- [x] appsettings requirement explicitly reported
- [x] Build passed
- [x] Targeted tests passed
- [x] Publish passed
- [ ] Full test suite passed; unrelated existing failures remain
- [ ] Push completed; not requested/performed
