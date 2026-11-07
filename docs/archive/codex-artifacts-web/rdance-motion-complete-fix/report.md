# RDance Motion Complete Fix

## 1. Baseline

- Repository: `nguyenduc216/todoX-Dashboard-SaaS`
- Branch: `feature/admin-job-monitor`
- Baseline commit: `dd6174912ac3e697ab5c7fb5442e4681dc8ea9b4`
- Previous comparison commit: `42f606538a1e6b807adb965f08efafeedc90d07e`
- Audit date: September 19, 2026

The baseline already contains the first-submit diagnostics and same-process queue gate from the previous fix. This report records the complete audit and validation of the requested RDance Motion flow.

## 2. Root Cause

### 2100

The provider returned HTTP 200 with `success=false`, `error="2100"`, and no task identifier. The parser previously risked losing the numeric provider code when `error` was a string. The current parser preserves `2100` as `Ai79TaskSubmitException.ErrorCode`, preserves the sanitized provider response, and does not return a fake task.

The available evidence does not establish that `2100` means file size. The failed and successful source media files were not present locally, so codec/container/FPS/duration/bitrate/MP4-structure comparison could not be performed.

### First submit / concurrency

The first-click symptom is consistent with concurrent queue requests passing the initial status check before either request persisted the queued state. The current queue path uses a per-Dance-Sell-job `SemaphoreSlim` before validation, point charging, operation creation, render enqueue, and job state update. A second request in the same process therefore observes the active status and receives `DANCE_SELL_JOB_ALREADY_ACTIVE`.

This is an application-process idempotency guard. It is not a distributed lock across multiple application instances. No schema or migration change was authorized, so no database lock/idempotency contract was introduced.

### Result button

The current `RDanceJobDetail.razor` already renders a centered result empty state with the prepared/reference image, prompt, motion readiness state, and `Tạo video`. The action calls the existing confirmation and queue flow. The audit found no second queue endpoint or direct polling-to-queue path.

### Polling

The Dashboard polling loop starts after successful queueing, runs at approximately three-second intervals, retains monitoring after transient reload errors, and stops only after a successfully loaded terminal job state. The backend RDance poll path uses the persisted `danceJob.ProviderTaskId`, not a logical request id, and persists the completed result URL through the existing completion service.

## 3. Evidence

- `Ai79TaskClient.ReadSubmitResultAsync` distinguishes provider rejection, HTTP/network failure, malformed JSON, missing task id, and accepted task responses.
- `DanceSellRenderHandler.Submit79AiAsync` handles `Ai79TaskSubmitException` as terminal provider failure. Timeout and HTTP exceptions after upload remain bounded retry cases.
- `DanceSellRepository.UpdateSubmittedAsync` writes `provider_task_id` only after a successful submit with a real provider task id.
- `DanceSellCompletionService.FailAsync` persists the terminal error and updates the render job without creating a provider task.
- `DanceSellRenderHandler.Poll79AiAsync` is entered only when a persisted provider task id exists.
- `DanceSellRepository.UpdateCompletedAsync` persists the result URL and terminal state.
- The RDance page source contains immediate image, TikTok, and local-video loading states and keeps polling after transient refresh errors.
- Production diagnostic job records were treated as read-only. No production job, billing record, provider task, or database state was mutated.

Confidence:

- `2100` parsing/no-fake-task behavior: high.
- Same-process first-submit race protection: high.
- Multi-instance duplicate protection: limited to the documented process-local gate.
- Result layout and polling source behavior: high from source and regression tests.
- Exact provider-side meaning of `2100`: unknown.

## 4. Changes

Already present in baseline `dd61749`:

- `TodoX.Web/Services/AiProviders/Ai79TaskClient.cs`
  - Preserves numeric provider error codes such as `2100`.
  - Keeps sanitized raw response data.
  - Does not synthesize a task id.
- `TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs`
  - Adds a per-job queue gate before charging and enqueueing.
  - Returns `DANCE_SELL_JOB_ALREADY_ACTIVE` for the second same-process request.
- `TodoX.Web.Tests/Ai79TaskClientTests.cs`
  - Regression coverage for the numeric `2100` response.
- `TodoX.Web/Tests/RDanceVideoFlowRegressionTests.cs`
  - Coverage for loading states, centered result empty state, polling behavior, and queue gating.
- `TodoX.Web/artifacts/codex/rdance-motion-first-submit-fix/report.md`
  - Previous fix report.

This continuation adds only this report:

- `TodoX.Web/artifacts/codex/rdance-motion-complete-fix/report.md`

No Razor page, database migration, billing logic, or provider payload was changed during this continuation.

## 5. Provider Contract

- Provider: `79ai`
- Model: `kling_video_motion_3`
- Endpoint: unchanged
- Form payload: unchanged
- Preserved fields include `subType=motion`, `mode=standard`, `ratio=default`, `projectId=default`, `backgroundSource=input_video`, `imageUrl`, and `videoUrl`.

No YEScale model/provider metadata was changed or added in this task.

## 6. Tests

Targeted command:

```text
dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Ai79TaskClientTests|FullyQualifiedName~RDanceVideoFlowRegressionTests|FullyQualifiedName~DanceSellRenderHandlerTests|FullyQualifiedName~RVideoSceneVideoRecoveryAndDiagnosticsTests"
```

Result:

- 81 passed
- 2 failed
- Failed tests are pre-existing and unrelated to RDance Motion:
  - `Ai79TaskClientTests.VideoSubmit_KeepsLegacyAsyncTaskAliases`
  - `Ai79TaskClientTests.VideoSubmit_UsesVerifiedCreateVideoContractWithExplicitStartAndEndFields`
- Both failures concern legacy `/create-video` task-id parsing, not `kling_video_motion_3`, RDance queueing, 2100 handling, or RDance polling.
- No new RDance regression failure was observed.

Broader RDance command:

```text
dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RDance"
```

Result:

- 32 passed
- 12 failed
- The failures are pre-existing source/layout expectation mismatches in legacy `RDanceFashionDemoPageTests` and `RDanceStagedBillingRegressionTests`.
- They do not identify a new failure in the RDance Motion changes from baseline `dd61749`.

The existing regression coverage verifies:

- Numeric `2100` preservation.
- No fake task id from a provider rejection response.
- Queue gate placement before charge and enqueue.
- Image/TikTok/local-video loading states.
- Centered result empty state and existing `ConfirmAndQueueAsync` action.
- Polling continuation after transient reload failure.
- Polling does not call `QueueRenderAsync`.
- Completed result persistence and duplicate completion protection through the existing completion tests.

Lint:

- No repository lint command is documented for this ASP.NET project. `git diff --check` was used for whitespace validation.

## 7. Build

Command:

```text
dotnet build TodoX.Dashboard.sln -c Release --no-restore -p:UseSharedCompilation=false
```

Result: success, 0 errors. Existing compiler warnings remain and were not introduced by this task.

## 8. Publish

Command:

```text
dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -p:UseSharedCompilation=false -o artifacts\publish\todox-dashboard
```

Result: success.

Output:

`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\TodoX.Web\artifacts\publish\todox-dashboard`

`TodoX.Web.dll` was present in the publish output. Publish output is excluded from the commit.

## 9. Deployment

NOT PERFORMED. IIS was not deployed or restarted, and no production service was restarted.

## 10. Database

NO production database mutation.

No migration, schema change, SQL execution, manual retry, billing mutation, provider task creation, or production job state update was performed.

## 11. Production Jobs

The following diagnostic identifiers were part of the read-only evidence scope:

- Failed Dance Sell job: `5ec35e57-4958-4991-b833-2e8c05e9debb`
- Failed render job: `5013d57f-aa63-408f-8a0f-57cbcff0c605`
- Successful comparable job: `bd85376f-4d73-4f96-8977-656551d053e6`
- Historical successful provider task: `5edc5c1bd4d0f1cf`

They were not mutated.

## 12. Remaining Risks

- The provider-side semantics of error `2100` remain undocumented by the available evidence.
- The failed and successful media binaries were unavailable locally, so media compatibility cannot be proven by `ffprobe`.
- The queue semaphore protects concurrent calls within one application process only. Multiple application instances would require a durable idempotency or locking mechanism, which would need an explicitly approved database design change.
- No live browser validation was performed at the requested four viewports.
- No real production render was executed. Production success is therefore not claimed.
