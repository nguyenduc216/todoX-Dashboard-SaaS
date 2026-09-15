# TDC-RDN-VERIFY-READY-APPROVE-QUEUE-20260909-008

## 1. Scope

Verify and correct the RDance Detail orchestration for:

```text
READY reference
  -> APPROVED reference
  -> ContinueAutoFinishAsync()
  -> DanceSell.QueueRenderAsync()
```

The change is limited to persisted reference selection used by the existing
state machine. No new render flow was introduced.

## 2. Baseline

- Branch: `feature/rdn-onepage-ui-revamp`
- HEAD before this task: `b5f0900`
- TDC-007 implementation commit: `b8a42b9`
- TDC-007 report finalization commit: `b5f0900`
- Working tree was clean before this task.

Existing flow before this fix:

```text
ReloadAsync
  -> list reference versions ordered by version_no DESC
  -> assign newest version to _latestReference
  -> ContinueAutoFinishAsync
```

## 3. Root cause

The repository returns reference versions newest first, but Detail always used
the newest version as `_latestReference`. If an older selected/usable version
was READY and a newer version was not usable, `ContinueAutoFinishAsync()` saw
the wrong version and stopped before approval.

The state transition itself already existed. The missing piece was resolving
the correct persisted selected/usable reference before invoking it.

## 4. Changed files

- `Components/Pages/RDanceJobDetail.razor`
- `Tests/RDanceCustomerStatusAndPointsRegressionTests.cs`
- `docs/TDC-RDN-VERIFY-READY-APPROVE-QUEUE-20260909-008.md`

## 5. State machine before

```text
ReloadAsync
  -> newest reference version only
  -> if newest is READY:
       approve
       reload
       continue
  -> if job is APPROVED + motion:
       QueueRenderAsync
  -> selected older READY reference could be ignored
```

## 6. State machine after

```text
ReloadAsync
  -> selected READY/APPROVED reference with URL
  -> fallback READY reference with URL
  -> fallback newest version
  -> ContinueAutoFinishAsync
       READY -> existing ApproveAsync/ApproveCharacterAsync
       APPROVED + motion -> existing QueueRenderAsync
```

The existing `RetryAsync()` Draft resume path from TDC-007 remains intact.

## 7. READY reference behavior

`ResolveAutoFinishReference()` now prioritizes the persisted selected
reference when it has a usable URL and READY/APPROVED status. For the normal
READY job state, `ContinueAutoFinishAsync()` calls the existing approval
service, reloads persisted state, re-enters itself, and then reaches
`DanceSell.QueueRenderAsync()`.

## 8. Approved reference behavior

When the persisted job is APPROVED, has motion media, and is not active or
completed, the existing `ContinueAutoFinishAsync()` path calls
`DanceSell.QueueRenderAsync()`. No reference regeneration is performed.

## 9. Draft retry behavior

The TDC-007 behavior remains:

```text
Draft
  -> ReloadAsync
  -> active/completed guard
  -> persisted motion validation
  -> AutoPrepareReferenceAsync for NotCreated/Generating
  -> ContinueAutoFinishAsync for usable reference
```

Motion is not uploaded again.

## 10. Idempotency

- Selected READY/APPROVED reference with URL: reused.
- Generating reference: existing AutoPrepare/poll path is reused.
- Active render: `IsActive` guard prevents another queue attempt.
- Completed render: no retry/resume action.
- Draft without motion: no queue attempt.
- No new `QueueRenderAsync()` implementation was added.

## 11. Acceptance test

Job:

`450355fe-2d0b-443a-8f5a-3b271c8fe0f8`

Repository-level acceptance path is covered: a Draft job with persisted motion,
usable selected reference, and no render job re-enters the existing approval and
queue orchestration without re-uploading motion.

Live DB/provider verification is unavailable in this workspace. The actual
production reference version, render job, provider task, and runtime status
were not inspected.

## 12. Tests

Targeted command:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RDanceCustomerStatusAndPointsRegressionTests"
```

Result:

```text
16 passed, 0 failed
```

Broader command:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RDance"
```

Result:

```text
36 passed, 3 failed
```

The three failures are PRE-EXISTING assertions in
`RDanceReferencePromptRegressionTests`:

- `DanceSellServicePrefersPersistedImagePromptAndRegeneratesVersions`
- `ManualRetryCreatesFreshMotionAttemptAndRebindsRenderInput`
- `ManualRetryReusesOnlyVerifiedAssetsWithCurrentMediaIdentity`

They are outside this task's changed files and unrelated to reference
selection/state-machine orchestration.

## 13. Build

```text
dotnet build TodoX.Web.csproj -c Release --no-restore
```

Result: PASS, `0 errors`.

The build emitted existing generated Razor nullable warnings.

## 14. Publish

```text
dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard
```

Result: PASS.

Output:

`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\TodoX.Web\artifacts\publish\todox-dashboard`

Publish artifacts are ignored and were not committed.

## 15. Diff check

```text
git diff --check
```

Result: PASS.

## 16. Protected areas

Untouched:

- One Page UI layout and CSS
- Create flow
- API contracts
- Database schema and migrations
- Reference/provider contracts
- 79AI and Kling integrations
- Render worker/provider execution
- Billing, points, and token wallet
- Storage architecture
- Upload implementation

## 17. Runtime verification

`NOT VERIFIED`

Live database/application/provider access was unavailable. Local tests verify
the repository-level orchestration contract only.

## 18. Commit

Commit SHA: recorded after commit.

Commit message:

`TDC-RDN-VERIFY-READY-APPROVE-QUEUE-20260909-008`

## 19. Push

Remote branch: `origin/feature/rdn-onepage-ui-revamp`

Remote SHA: recorded after push.

Push result: recorded after push.
