# RVIDEO 79AI Submit and Poll Diagnostics

## Root Cause

`Ai79TaskStatusNormalizer.Normalize` treats an unrecognized provider status as
`RUNNING`. The raw 79AI poll response `status=NOT_RESOURCES` therefore reached
the RVIDEO adapter as `VideoProviderTaskStatus.Processing`, producing the
misleading `SCENE_VIDEO_PROVIDER_PROCESSING` event.

The RVIDEO adapter now preserves the raw provider status supplied by
`Ai79TaskClient` and classifies `NOT_RESOURCES` as
`VideoProviderTaskStatus.ResourceUnavailable`. This classification is limited
to RVIDEO and does not alter shared 79AI normalization semantics for other
consumers.

## Confirmed Submit Call Chain

1. `SceneVideoWorkerHandler.HandleAsync` writes `RVIDEO_VIDEO_SUBMIT_STARTED`
   with the active candidate's `policy.Model` and `policy.Mode`.
2. `Ai79VideoGenerationProviderAdapter.SubmitAsync` resolves runtime and
   uploads the source/reference images.
3. `RVideo79AiVideoService.SubmitAsync` builds the existing 79AI video form
   contract using the active candidate model, mode, duration, ratio, resolution
   and normalized image URLs.
4. It emits `RVIDEO_VIDEO_HTTP_SUBMIT_REQUEST` immediately before invoking
   `IAi79TaskClient.SubmitAsync`.
5. `Ai79TaskClient.SubmitAsync` calls `HttpClient.PostAsync` at
   `BaseUrl + SubmitPath`; the runtime submit path remains `/create-video`.
6. On a parsed response, `RVIDEO_VIDEO_HTTP_SUBMIT_RESPONSE` is emitted. On an
   invalid JSON response, `RVIDEO_VIDEO_HTTP_SUBMIT_RESPONSE_PARSE_FAILED` is
   emitted and the existing unknown-submission semantics remain in force.

The request diagnostic carries `renderJobId`, project/scene correlation,
provider code, actual model/mode, duration, ratio, resolution, image count,
the exact absolute image URLs, endpoint, and a maximum 240-character prompt
preview. The response diagnostic adds HTTP status, task ID, video base ID, raw
provider status, countTasks, provider message, and sanitized response JSON.
No access token, Authorization header, credential, API key, or password is
included. Provider response JSON continues through the existing sanitizer.

## Image URL Handling

`ResolveProviderImageUrl` keeps absolute HTTP/HTTPS URLs unchanged. Relative
values such as `/uploads/a.png` and `uploads/a.png` are resolved with the first
configured `TodoX:PublicBaseUrl`, `App:PublicBaseUrl`, or
`Storage:PublicBaseUrl`, using `Uri` joining to avoid duplicate slashes. No
host is hard-coded.

## Actual Fallback Candidate

The outbound request receives `policy.Model` and `policy.Mode`, not the
original input model metadata. A job originally requested as `veo_omni` with
the active fallback candidate `veo_3_1/fast` sends `model=veo_3_1` and
`mode=fast`; the request diagnostic records that same pair.

## Poll Behavior

`PENDING`, `ACTIVE`, and `PROCESSING` remain processing states. A known task
whose raw status is `NOT_RESOURCES` emits
`RVIDEO_VIDEO_PROVIDER_RESOURCES_UNAVAILABLE`, is marked pending
reconciliation, and has its existing provider task deferred for another poll.
It does not create a new video, does not clear identifiers, and does not
trigger a fallback candidate in this change.

## Scope Kept Unchanged

No fallback order or model policy, endpoint, form payload fields, retry policy,
provider identifier persistence, polling timeout/reconciliation limit, billing,
Timelapse, DanceSell, or database schema was changed. No migration is needed.

## Files Changed

- `TodoX.Web/Services/AiProviders/Ai79TaskClient.cs`
- `TodoX.Web/Services/VideoRender/VideoGenerationProviderAdapter.cs`
- `TodoX.Web/Services/VideoRender/Ai79VideoGenerationProviderAdapter.cs`
- `TodoX.Web/Services/VideoRender/RVideo79AiVideoService.cs`
- `TodoX.Web/Services/VideoRender/SceneVideoWorkerHandler.cs`
- `TodoX.Web.Tests/RVideoSceneVideoRecoveryAndDiagnosticsTests.cs`
- `TodoX.Web/Tests/RVideoVideoHotfixTests.cs`

## Validation

- `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~RVideoSceneVideoRecoveryAndDiagnosticsTests"`: passed, 23/23.
- The broader RVIDEO/79AI filtered test run had 62 pass and 2 known legacy
  `Ai79TaskClientTests` failures. Those tests provide responses containing only
  legacy `request_id`, which the previously-established 79AI video identifier
  contract rejects because it requires the polling `id_base`; this patch did
  not change that behavior.
- `dotnet build TodoX.Web/TodoX.Web.csproj --configuration Release --no-restore`:
  passed, 0 errors and 45 existing Razor nullable warnings.
- `dotnet publish TodoX.Web/TodoX.Web.csproj --configuration Release --no-build
  --output artifacts/publish/todox-dashboard`: passed; output directory created.
- Commit: recorded by the final commit below.

## Remaining Limitations

The new request event proves that the application began the outbound submit
call; a transport failure can still prevent 79AI from receiving it. A received
HTTP response is captured in the response event when available. Whether an
existing `NOT_RESOURCES` task should eventually move to the next fallback
candidate remains a separate policy decision and is intentionally not included
in this patch.
