# TDC-RDN-REFERENCE-MODE-AUTOPREPARE-GUARD-20260909-006

## Root cause

`RDanceJobDetail.razor` called `References.AutoPrepareAsync()` for every
automatic reference attempt. This made Detail inconsistent with Create:
character-only jobs were routed through the product/reference-generation
service instead of the existing character approval flow.

## Changed files

- `TodoX.Web/Components/Pages/RDanceJobDetail.razor`
- `TodoX.Web/Tests/RDanceCustomerStatusAndPointsRegressionTests.cs`
- `docs/TDC-RDN-REFERENCE-MODE-AUTOPREPARE-GUARD-20260909-006.md`

## Flow

Before:

```text
AutoPrepareReferenceAsync
  -> ReloadAsync
  -> optional provider readiness check
  -> References.AutoPrepareAsync for all reference modes
  -> ContinueAutoFinishAsync
```

After:

```text
AutoPrepareReferenceAsync
  -> ReloadAsync
  -> return when reference is already Ready/Approved with URL
  -> return when reference is Generating or mode is DirectReference
  -> character-only: References.ApproveCharacterAsync
  -> character+product: EnsureReferenceProviderReady
       -> References.AutoPrepareAsync
  -> ContinueAutoFinishAsync
```

## Reference behavior

- Character-only: an existing character image and no complete product image
  call `ApproveCharacterAsync()` automatically. No manual approval click is
  required.
- Character+Product: both complete inputs call the existing
  `AutoPrepareAsync()` generation flow.
- Direct reference: the automatic character/product branch is skipped and
  the existing direct-reference behavior is preserved.

## Motion sources

MP4 upload and TikTok staging continue to invoke the same Detail
`AutoPrepareReferenceAsync()` path after the persisted job is reloaded. The
source mode does not create a second reference implementation.

## Idempotency and render ownership

Already ready/approved references are not regenerated. Generating references
are not started again. Detail remains the only render orchestration owner:
`ContinueAutoFinishAsync()` calls the existing confirmation/queue path and
`DanceSell.QueueRenderAsync()`; Create does not enqueue renders.

## Validation

Targeted suite:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RDanceCustomerStatusAndPointsRegressionTests"
```

Result: PASS, 14 passed, 0 failed.

Broader RDance filter:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RDance"
```

Result: 34 passed, 3 failed. The three failures are existing
`RDanceReferencePromptRegressionTests` assertions outside this reference-mode
guard change.

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

Database schema and migrations, API/provider contracts, 79AI/Kling
integration, render worker/provider implementation, video generation,
billing, points, token wallet, and storage architecture were not changed.
Publish output is ignored and is not committed.

Commit SHA and push result are recorded in the final completion message.
