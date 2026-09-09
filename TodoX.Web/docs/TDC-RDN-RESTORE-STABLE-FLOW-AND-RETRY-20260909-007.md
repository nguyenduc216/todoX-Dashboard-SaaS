# TDC-RDN-RESTORE-STABLE-FLOW-AND-RETRY-20260909-007

## Root cause

`RDanceJobDetail.RetryAsync()` always called `DanceSell.RetryAsync()`.
That service intentionally handles jobs that already have a retryable render
job. Older or interrupted RDance jobs that remained `Draft`, with persisted
motion and reference state but no render job, therefore stopped with
`DANCE_SELL_RETRY_NOT_ALLOWED`.

## Stable baseline identified

The existing stable orchestration is state-driven in
`RDanceJobDetail.razor`:

- `ReloadAsync()` reloads the RDance job, render job, and reference versions.
- `AutoPrepareReferenceAsync()` reuses or prepares the persisted reference.
- `ContinueAutoFinishAsync()` approves a ready reference and queues render.
- `DanceSell.QueueRenderAsync()` remains the only normal render queue path.
- `DanceSell.RetryAsync()` remains the retry path for an existing failed or
  cancelled render attempt.

The previous re-entry fix already restored the READY -> APPROVED ->
AUTO-FINISH transition. This task restores the missing Draft retry/resume
entry point without reverting the One Page UI.

## Exact orchestration changes

Changed `Components/Pages/RDanceJobDetail.razor`:

- Reload persisted state before retry decisions.
- Return without action for active render jobs and completed jobs.
- For Draft jobs, require persisted motion media and URL.
- Resume `NotCreated` or `Generating` references through the existing
  `AutoPrepareReferenceAsync()` path.
- Resume other Draft states through the existing
  `ContinueAutoFinishAsync()` path.
- Continue using `DanceSell.RetryAsync()` only for Failed, Timeout, or
  cancelled render states.
- No upload, reference generation implementation, or new queue method was
  added.

Changed `Tests/RDanceCustomerStatusAndPointsRegressionTests.cs`:

- Added a regression assertion that Draft retry resumes persisted state,
  does not upload motion again, and protects active renders.

## New-job flow

The existing state machine remains:

```text
persisted inputs
  -> reference resolution
  -> reference approval
  -> ContinueAutoFinishAsync
  -> DanceSell.QueueRenderAsync
  -> existing render worker/provider flow
```

Character-only reference resolution continues through
`ApproveCharacterAsync()`. Character plus product continues through
`AutoPrepareAsync()`. Existing Generating versions continue through the
service's existing polling/reuse mechanism.

## Old-job retry flow

```text
RetryAsync
  -> ReloadAsync
  -> active/completed guard
  -> Draft + missing motion: return
  -> Draft + missing/generating reference: AutoPrepareReferenceAsync
  -> Draft + usable reference: ContinueAutoFinishAsync
  -> Failed/Timeout/Cancelled render: DanceSell.RetryAsync
```

Inputs are not uploaded again, and usable reference versions are not
regenerated.

## Duplicate-render protection

The UI returns when the persisted RDance job or render job is active.
Normal render creation still goes through the existing
`DanceSell.QueueRenderAsync()` implementation, which rejects active
RDance render states. No second render queue implementation was introduced.

## Regression job

Mandatory job:

`450355fe-2d0b-443a-8f5a-3b271c8fe0f8`

The repository-side recovery path now supports its documented state:
persisted character, motion, and READY reference with no render job will
re-enter approval and auto-finish from the detail page.

Live authenticated application/database access was not available in this
workspace, so the following runtime values were not independently inspected:

- RDance job status
- reference version ID and status
- render job ID and status
- provider task ID and provider status
- error code and error message

No claim of provider submission or database mutation is made from local
build/test results alone.

## Tests

Targeted:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RDanceCustomerStatusAndPointsRegressionTests"
15 passed, 0 failed
```

Broader RDance filter:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RDance"
35 passed, 3 failed
```

The three failures are pre-existing source assertions in
`RDanceReferencePromptRegressionTests` and are unrelated to this retry
orchestration change.

## Build

```text
dotnet build TodoX.Web.csproj -c Release --no-restore
PASS - 0 errors
```

## Publish

```text
dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard
PASS
```

Output directory:

`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\TodoX.Web\artifacts\publish\todox-dashboard`

Publish artifacts remain ignored and were not staged.

## Lint and diff checks

`git diff --check`: PASS.

`dotnet format --verify-no-changes` reports existing whitespace violations
across unrelated repository files. Those files were not modified.

## Protected areas untouched

- Database schema and migrations
- API contracts
- Provider contracts and integrations
- Render worker and video generation implementation
- Billing, points, and token wallet
- Storage architecture
- One Page UI layout and feature flags

## Commit and push

Commit message:

`TDC-RDN-RESTORE-STABLE-FLOW-AND-RETRY-20260909-007`

Commit SHA and push result are recorded after the commit/push commands.
