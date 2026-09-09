# TDC-RDN-AUTOFINISH-REFERENCE-REENTRY-FIX-20260909-007

## Root cause

Three orchestration gaps were found in `RDanceJobDetail.razor`:

1. Opening an unfinished draft only called `ReloadAsync()` and
   `ContinueAutoFinishAsync()`. A draft with motion ready and a missing
   reference never entered reference preparation.
2. Polling called `AutoPrepareReferenceAsync()` for a generating reference,
   but that method returned before calling `References.AutoPrepareAsync()`.
   The existing service poll/reuse logic therefore never ran.
3. A Ready reference was approved, then reloaded while `_autoFinishing` was
   still set. The nested continuation returned and the render queue was not
   reached in that invocation.

## Files changed

- `TodoX.Web/Components/Pages/RDanceJobDetail.razor`
- `TodoX.Web/Tests/RDanceCustomerStatusAndPointsRegressionTests.cs`
- `docs/TDC-RDN-AUTOFINISH-REFERENCE-REENTRY-FIX-20260909-007.md`

## Flow before

```text
Open Detail
  -> ReloadAsync
  -> ContinueAutoFinishAsync
  -> reference NotCreated: return
  -> StartPolling
  -> NotCreated is not generating and job is not active
  -> no automatic reference resolution
```

For a generating reference:

```text
PollLoopAsync
  -> AutoPrepareReferenceAsync
  -> IsReferenceGenerating
  -> return
  -> ContinueAutoFinishAsync
  -> reference is not approved
  -> return
```

For a Ready reference:

```text
ContinueAutoFinishAsync
  -> approve reference
  -> ReloadAsync while _autoFinishing
  -> nested ContinueAutoFinishAsync returns
  -> outer method returns without queueing
```

## Flow after

```text
Open Detail
  -> ReloadAsync
  -> if draft + motion ready + character + missing/generating reference
  -> AutoPrepareReferenceAsync
```

```text
PollLoopAsync
  -> ShouldResumeAutoReference
  -> AutoPrepareReferenceAsync
  -> generating reference:
       References.AutoPrepareAsync
       -> existing PollGeneratingReferenceAsync/reuse behavior
```

After a Ready reference is approved, `_autoFinishing` is released before a
second `ContinueAutoFinishAsync()` evaluation. An Approved reference with
ready motion can therefore use the existing `DanceSell.QueueRenderAsync()`
path.

## Reference modes

### Character-only

Character exists and no complete product image exists:

```text
ApproveCharacterAsync()
  -> reference Approved
  -> ContinueAutoFinishAsync()
  -> existing QueueRenderAsync path
```

`AutoPrepareAsync()` is not used for this branch.

### Character + Product

Both character and product images exist:

```text
EnsureReferenceProviderReady()
  -> AutoPrepareAsync()
  -> existing reference generation/reuse/poll behavior
```

### Direct reference

Direct-reference jobs are excluded from automatic character/product
preparation and retain the existing direct-reference behavior.

## Generating, Ready, and Approved

- `Generating`: the existing `AutoPrepareAsync()` service is now allowed to
  poll/reuse the current generating version. No new version is created by the
  Detail orchestration.
- `Ready`: the existing approval method runs, then continuation is evaluated
  again after `_autoFinishing` is released.
- `Approved`: if motion is Ready and no job/render is active,
  `ContinueAutoFinishAsync()` reaches `DanceSell.QueueRenderAsync()`.

## Duplicate protection

Automatic entry is restricted to draft jobs with ready motion, available
character input, no direct-reference mode, and a null/draft render job.
Active render statuses are included in `IsActive`. Continuation also stops
for active, completed, failed, timeout, or cancelled states. Existing
`AutoPrepareAsync()` reference reuse/locking and `QueueRenderAsync()` service
validation remain unchanged.

## Old regression job

For `450355fe-2d0b-443a-8f5a-3b271c8fe0f8`, the code path now supports:

```text
Open Detail
  -> detect draft + character + motion Ready + reference NotCreated
  -> AutoPrepareReferenceAsync
  -> ApproveCharacterAsync
  -> ContinueAutoFinishAsync
  -> QueueRenderAsync
```

Runtime database execution was not performed in this environment. Therefore
this report does not claim that the specific job has actually been updated or
queued; that requires live authenticated application/database evidence.

## Validation

Targeted tests:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RDanceCustomerStatusAndPointsRegressionTests"
```

Result: PASS, 14 passed, 0 failed.

Broader RDance tests:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RDance"
```

Result: 34 passed, 3 failed. The three failures are existing
`RDanceReferencePromptRegressionTests` assertions outside this re-entry
orchestration change.

Build:

```text
dotnet build TodoX.Web.csproj -c Release --no-restore
```

Result: PASS, 0 errors. Existing generated Razor nullable warnings remain.

Publish:

```text
dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard
```

Result: PASS. Output:
`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

`git diff --check`: PASS.

## Protected areas

No database schema or migration, API contract, provider contract, 79AI/Kling
integration, render worker, video generation implementation, billing, points,
token wallet, or storage architecture was changed.
