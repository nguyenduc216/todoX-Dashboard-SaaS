# RVideo Core Identity Resolution Fix

## 1. Root cause addressed

Core execution routing previously used the persisted catalog `serviceCode` directly as the execution-adapter key. A customer-facing RVideo catalog service such as `NUOI_DAY_CON_VIDEO` has `service_type = rvideo`, while the RVideo execution adapter key is `RVIDEO`. The router therefore rejected the Core job before adapter dispatch.

## 2. Architecture before

```text
Core job serviceCode = NUOI_DAY_CON_VIDEO
-> CoreExecutionRouter lookup by NUOI_DAY_CON_VIDEO
-> no adapter found
-> Core job failed
```

## 3. Architecture after

```text
Core job serviceCode = NUOI_DAY_CON_VIDEO
-> authoritative catalog lookup by service_id
-> catalog service_type = rvideo
-> execution adapter resolution = RVIDEO
-> CoreExecutionRouter lookup by RVIDEO
-> RVideoCoreExecutionAdapter
```

## 4. New resolution contract

`ICoreExecutionAdapterResolver` separates catalog identity from execution identity.

- `rvideo` catalog services resolve to `RVIDEO`.
- `timelapse` catalog services retain direct adapter resolution by catalog service code.
- Unmapped service types return a controlled failure reason.

The Core job snapshot continues to retain its original catalog `serviceCode`. The router creates a dispatch context with the separately resolved execution adapter key only for adapter lookup and invocation.

## 5. Files changed

- `TodoX.Web/Services/Platform/CoreExecutionAdapterResolver.cs`
- `TodoX.Web/Services/Platform/CorePlatformContracts.cs`
- `TodoX.Web/Services/Platform/CoreExecutionRouter.cs`
- `TodoX.Web/Services/Platform/CorePlatformServiceCollectionExtensions.cs`
- `TodoX.Web/Services/Platform/CoreServiceJobHandler.cs`
- `TodoX.Web.Tests/CorePlatformContractTests.cs`
- `TodoX.Web.Tests/CoreExecutionLifecycleTests.cs`
- `TodoX.Web.Tests/RVideoCoreExecutionTests.cs`
- `TodoX.Web.Tests/ConstructionTimelapseCoreTests.cs`

## 6. Exact methods changed

- `CoreExecutionAdapterResolver.Resolve`
- `CoreExecutionRouter.CanHandle`
- `CoreExecutionRouter.DispatchAsync`
- `CoreServiceJobHandler.HandleAsync`
- `CorePlatformServiceCollectionExtensions.AddTodoXCorePlatform`

## 7. Why catalog serviceCode is preserved

The Core envelope and `render.render_jobs` continue to hold the original catalog service code for service ownership, pricing, audit, reporting, and correlation. The resolution boundary does not rewrite persisted catalog identity.

## 8. Why RVIDEO is execution identity

`RVideoCoreExecutionAdapter.ServiceCode` remains `RVIDEO`. It is the registered execution adapter for the RVideo service family. Multiple catalog services with `service_type = rvideo` can therefore use the same execution implementation without hardcoding individual catalog service codes.

## 9. Tests added

- Any RVideo catalog service type maps to `RVIDEO`, including `NUOI_DAY_CON_VIDEO` and `ANOTHER_RVIDEO_SERVICE`.
- `CONSTRUCTION_VIDEO` with `timelapse` still maps directly to `CONSTRUCTION_VIDEO`.
- Unknown catalog service types fail in a controlled manner.
- A Core job retaining `serviceCode = NUOI_DAY_CON_VIDEO` dispatches the RVideo adapter with execution key `RVIDEO`.
- Existing direct router and Timelapse tests were updated for the explicit resolution contract.

## 10. Test results

Focused Core/RVideo/Timelapse tests:

```text
Passed: 64, Failed: 0
```

Focused RVideo scene/provider/claim regression tests:

```text
Passed: 20, Failed: 0
```

Full solution tests were executed:

```text
Passed: 920, Failed: 18
```

The 18 failures are pre-existing, outside this change, and are concentrated in RDance UI/source-contract tests, DanceSell reference prompt tests, unrelated static-image billing source checks, favorite-services source checks, and an existing Core progress source-contract expectation. No provider, fallback, billing, retry, scene-worker, or RDance source file was modified by this change.

## 11. Build result

```text
dotnet build TodoX.Dashboard.sln -c Release --no-restore -p:UseSharedCompilation=false
Succeeded: 0 errors, 46 existing warnings
```

## 12. Publish result

```text
dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -p:UseSharedCompilation=false -o artifacts\publish\todox-dashboard
Succeeded
Output: artifacts\publish\todox-dashboard
```

## 13. Diff safety review

`git diff --check` passed. No database migration, schema, provider route, 79AI endpoint/payload, fallback policy, scene rendering, billing logic, retry policy, or RVideo adapter implementation was changed.

`dotnet format --verify-no-changes` was run and failed on existing repository-wide whitespace violations in unrelated files. No automatic formatting changes were applied.

## 14. Remaining 79AI fallback issue

79AI fallback was not changed. `ResolveFallbackCandidates`, `veo_omni`, `veo_3_1`, `fast`, `lite`, provider routing, provider retry, and billing fallback behavior remain untouched.

## 15. Commit SHA

`1977402` (`Fix Core catalog service adapter resolution`)
