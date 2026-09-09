# TDC-RDN-NEW-UI-RENDER-PATH-AUDIT-FIX-20260909-012

## 1. Scope

Fix the RDance New UI render-button path for terminal jobs. The fix reuses the
existing retry orchestration and does not create a new render pipeline.

## 2. Regression Evidence

Regression job:

`450355fe-2d0b-443a-8f5a-3b271c8fe0f8`

The provider/upload path was not changed in this task. The code comparison
identified a New UI terminal-state routing gap before provider execution.

## 3. OLD UI Call Graph

Legacy `DanceSell.razor`:

```text
Tạo video
-> QueueRenderAsync()
-> DanceSellService.UpdateBusinessAsync()
-> DanceSellService.QueueRenderAsync()
-> render job
-> DanceSellRenderHandler
-> 79AI
```

Legacy retry:

```text
Retry
-> DanceSellService.RetryAsync()
-> new render job and provider operation
-> render worker
-> DanceSellRenderHandler
-> 79AI
```

## 4. NEW UI Call Graph Before Fix

`RDanceJobDetail.razor` bound the New UI button to:

```text
Tạo video
-> ConfirmAndQueueAsync()
-> if (_autoFinish)
-> ContinueAutoFinishAsync()
-> terminal-state guard returns
-> no RetryAsync()
-> no QueueRenderAsync()
```

For draft/ready jobs, the existing path was:

```text
ConfirmAndQueueAsync()
-> UpdateBusinessAsync()
-> reload persisted job
-> validate approved reference
-> DanceSell.QueueRenderAsync()
-> render job
-> worker
-> provider
```

## 5. Side-by-Side Comparison

| Check | OLD UI | NEW UI before fix | New UI after fix |
|---|---|---|---|
| Normal render button | `QueueRenderAsync` | `ConfirmAndQueueAsync` | unchanged |
| Auto-finish | not used by legacy button | `ContinueAutoFinishAsync` | unchanged for non-terminal jobs |
| Failed/timeout job | `RetryAsync` | `ContinueAutoFinishAsync` returned | `RetryAsync` |
| Cancelled core job | retry path | `ContinueAutoFinishAsync` returned | `RetryAsync` |
| New render attempt | yes for retry | no | yes through existing service |
| Provider path | existing worker | not reached | existing worker |

## 6. Exact Root Cause

File:

`TodoX.Web/Components/Pages/RDanceJobDetail.razor`

Method:

`ConfirmAndQueueAsync()`

Logic difference:

- `CanRender` allowed a non-active failed/timeout job to show the New UI
  “Tạo video” button.
- When `_autoFinish` was enabled, `ConfirmAndQueueAsync()` called
  `ContinueAutoFinishAsync()`.
- `ContinueAutoFinishAsync()` explicitly returned for
  `DanceSellJobStatuses.Failed`, `DanceSellJobStatuses.Timeout`, or cancelled
  render jobs.
- Therefore the New UI terminal button stopped without calling either
  `RetryAsync()` or `QueueRenderAsync()`.

The legacy UI used `RetryAsync()` for failed/timeout jobs, so it created a new
render attempt and continued to the worker.

## 7. Exact Files Changed

- `TodoX.Web/Components/Pages/RDanceJobDetail.razor`
- `TodoX.Web.Tests/RDanceFashionDemoPageTests.cs`
- `docs/TDC-RDN-NEW-UI-RENDER-PATH-AUDIT-FIX-20260909-012.md`

## 8. Exact Methods Changed

- `RDanceJobDetail.razor::ConfirmAndQueueAsync`
- Added source regression assertion:
  `RDanceNewUiCreateButtonRoutesTerminalJobsThroughExistingRetryFlow`

No service or provider method was changed.

## 9. Why OLD UI Worked

The legacy result/retry controls call `RetryAsync()` for failed, timeout, and
cancelled jobs. `DanceSellPhase2Service.RetryAsync()` creates a new provider
operation and render job, then rebinds the DanceSell job to that attempt.

## 10. Why NEW UI Failed

The New UI “Tạo video” button entered the auto-finish branch first. For terminal
jobs, auto-finish intentionally returns, but the button handler had no terminal
job fallback to the existing retry path.

## 11. Fix

`ConfirmAndQueueAsync()` now routes failed, timeout, and cancelled jobs directly
to the existing `RetryAsync()` method before evaluating `_autoFinish`.

Normal draft/ready behavior remains unchanged.

## 12. Why the Fix Is Minimal

- One terminal-state guard in the existing UI event handler.
- Reuses existing `RetryAsync`, `DanceSellPhase2Service.RetryAsync`, render-job
  creation, and worker execution.
- No new queue method, provider call, endpoint, payload, or state machine.

## 13. Retry Behavior

Manual retry continues to create a fresh render attempt and provider operation
through the existing `DanceSell.RetryAsync` service. The retry architecture was
not modified.

## 14. Reference Behavior

Prepared reference validation and approval behavior were not changed.

## 15. Motion Behavior

Motion media validation, resolution, provider upload, reuse, and verification
were not changed by TDC-012.

## 16. Render Behavior

The New UI now reaches the same existing render orchestration as the legacy UI:

```text
terminal New UI button
-> RDanceJobDetail.RetryAsync()
-> DanceSell.RetryAsync()
-> fresh render attempt
-> RenderJobWorker
-> DanceSellRenderHandler
```

## 17. Provider Behavior

79AI client, upload implementation, verification, Motion Control payload,
endpoint, model, and provider contract were not changed.

## 18. Tests

Targeted regression:

```text
dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore
--filter FullyQualifiedName~RDanceNewUiCreateButtonRoutesTerminalJobsThroughExistingRetryFlow
```

Result: PASS.

The requested Phase 1B suite completed with 418 passed and 9 failures. The
failures are outside this task, including existing video/voice assertions and
older RDance source expectations. They were not changed as part of TDC-012.

## 19. Build

Command:

```text
dotnet build TodoX.Web.csproj -c Release --no-restore
```

Result: PASS, 0 errors. Existing generated Razor nullable warnings remain.

## 20. Publish

Command:

```text
dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard
```

Result: PASS.

Output:

`TodoX.Web/artifacts/publish/todox-dashboard`

## 21. Diff Check

Command:

```text
git diff --check
```

Result: PASS.

## 22. Protected Areas

OUT OF SCOPE - DETECTED BUT NOT MODIFIED:

- RDance Create UI and layout
- database schema and migrations
- 79AI upload implementation
- 79AI provider contract and payload
- KIE
- billing and points
- token wallet
- render worker architecture
- storage architecture
- API contracts

## 23. Runtime Verification

No production retry or live provider render was executed.

`RUNTIME VERIFICATION: NOT VERIFIED`

## 24. Commit

Commit: PENDING

## 25. Push

Remote: `origin/feature/rdn-onepage-ui-revamp`

Result: PENDING

## 26. Working Tree

Result: PENDING
