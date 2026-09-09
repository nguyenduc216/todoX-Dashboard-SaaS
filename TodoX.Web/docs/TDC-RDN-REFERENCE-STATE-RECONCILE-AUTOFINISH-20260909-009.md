# TDC-RDN-REFERENCE-STATE-RECONCILE-AUTOFINISH-20260909-009

## Scope

Reconcile an existing selected READY/APPROVED RDance reference when the
persisted job fields are stale, then allow the existing auto-finish lifecycle
to continue to render queueing.

Protected areas intentionally untouched:

- UI and routing
- Public API contracts
- Database schema and migrations
- Reference provider integration
- Motion/video generation
- Render pipeline
- Billing and points

## Baseline

- Branch: `feature/rdn-onepage-ui-revamp`
- Baseline HEAD: `3cd3b83e96dceec909459fdb7e03a56727a20af0`
- Regression job: `450355fe-2d0b-443a-8f5a-3b271c8fe0f8`

## Root Cause

`DanceSellReferenceImageService.ApproveCharacterAsync()` always created a new
local-composite reference version after listing existing versions. When a
selected usable READY reference existed but the job still had
`prepared_reference_status = NotCreated` and no bound media/url, the method
created a duplicate version instead of reconciling the existing reference.

## Changed Files

- `Services/DanceSell/DanceSellPhase2Services.cs`
  - Updated `ApproveCharacterAsync()`.
  - Reuses the selected usable character-only READY/APPROVED reference first.
  - Falls back to the existing version-creation path when no usable version
    exists.
- `Tests/RDanceCustomerStatusAndPointsRegressionTests.cs`
  - Added source-level regression coverage for the reuse, selection, approval,
    and fallback ordering.
- `docs/TDC-RDN-REFERENCE-STATE-RECONCILE-AUTOFINISH-20260909-009.md`
  - Added this report.

## Before and After Flow

Before:

```text
ReloadAsync
  -> ResolveAutoFinishReference
  -> ContinueAutoFinishAsync
  -> ApproveCharacterAsync
  -> CreateReferenceVersionAsync
  -> SelectReferenceVersionAsync
  -> UpdateReferenceStatusAsync(Approved)
```

After:

```text
ReloadAsync
  -> ResolveAutoFinishReference
  -> ContinueAutoFinishAsync
  -> ApproveCharacterAsync
      -> ListReferenceVersionsAsync
      -> selected usable READY/APPROVED version
      -> EnsureReferenceVersionRatioMatchesJob
      -> SelectReferenceVersionAsync
      -> UpdateReferenceStatusAsync(Approved, existing media/object/url)
      -> reload job
  -> ContinueAutoFinishAsync
  -> DanceSell.QueueRenderAsync
```

If no compatible usable reference exists, the existing
`CreateReferenceVersionAsync()` fallback remains unchanged.

## Reconciliation Rules

The reusable version must:

- Match the current character media identity.
- Have no product media.
- Be READY or APPROVED.
- Have a media ID and public URL.
- Prefer `IsSelected == true`.
- Pass the existing ratio compatibility guard.

The job is synchronized through the existing repository update method. No new
repository method, endpoint, schema, or API contract was added.

## Duplicate and Retry Protection

- Existing usable references are selected and approved instead of regenerated.
- The provider is not called by this reconciliation branch.
- Motion is not uploaded again.
- Queueing continues through the existing `ContinueAutoFinishAsync()` and
  `DanceSell.QueueRenderAsync()` path.
- Existing render-active/completed guards remain in `RDanceJobDetail.razor`.
- Draft retry behavior remains unchanged.

## Regression Job

For job `450355fe-2d0b-443a-8f5a-3b271c8fe0f8`, the corrected code path
supports:

```text
existing selected READY character reference
  -> reconcile stale job fields
  -> APPROVED
  -> ContinueAutoFinishAsync()
  -> QueueRenderAsync()
```

Runtime database/provider verification was not available in this environment:
`NOT VERIFIED`.

## Validation

Passed:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RDanceCustomerStatusAndPointsRegressionTests"
17 passed, 0 failed

dotnet build TodoX.Web.csproj -c Release --no-restore
Passed with 45 pre-existing Razor nullable warnings

git diff --check
Passed
```

The broader RDance filter reported `37 passed, 3 failed`. The three failures
are pre-existing assertions in `RDanceReferencePromptRegressionTests` and are
outside this task:

- `DanceSellServicePrefersPersistedImagePromptAndRegeneratesVersions`
- `ManualRetryCreatesFreshMotionAttemptAndRebindsRenderInput`
- `ManualRetryReusesOnlyVerifiedAssetsWithCurrentMediaIdentity`

Publish:

```text
dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard
Passed with 45 pre-existing Razor nullable warnings.
Output: artifacts/publish/todox-dashboard
```

## Commit and Push

- Commit message: `TDC-RDN-REFERENCE-STATE-RECONCILE-AUTOFINISH-20260909-009`
- Commit SHA: `0455d1a`
- Push result: `SUCCESS` to `origin/feature/rdn-onepage-ui-revamp`
- Worktree status: unrelated pre-existing changes are preserved and not staged.
