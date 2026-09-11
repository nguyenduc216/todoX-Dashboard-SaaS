# TDC-RDN-JOB-NOT-FOUND-AUDIT-20260911-001

Audit target: `bdb30808-6098-48ab-a47c-fbd6fe29aaa7`

Scope: RDance route, detail lifecycle, `ReloadAsync`, `DanceSell.GetAsync`, repository lookup, ownership/security and tenant filtering.

Production code, database and configuration were not modified. No build, publish, commit or push was performed.

## 1. Actual Call Graph

```text
/jobs/rdance/{JobId:guid}
  -> RDanceJobDetail.OnParametersSetAsync()
     -> ReloadAsync()
        -> ProviderCatalog.GetDefaultRouteAsync(ReferenceImage)
        -> ProviderCatalog.GetDefaultRouteAsync(MotionVideo)
        -> DanceSell.GetAsync(JobId, AuthState.CurrentUser)
           -> RequireOwnedJobAsync(JobId, user)
              -> DanceSellRepository.GetByIdAsync(JobId)
                 -> SELECT ... FROM dance_sell.dance_sell_jobs
                    WHERE id=@id
              -> if row == null:
                    throw DANCE_SELL_NOT_FOUND
              -> if !DanceSellSecurity.CanAccess(user, row):
                    throw DANCE_SELL_UNAUTHORIZED
              -> return DanceSellJobDto
        -> RenderJobs.GetAsync(_job.RenderJobId), when RenderJobId exists
        -> DanceRepo.ListReferenceVersionsAsync(_job.Id)
        -> RefreshEstimateAsync()
        -> ContinueAutoFinishAsync()
     -> StartPolling()
```

The new detail page uses the service directly through dependency injection. There is no separate detail-page HTTP API call in this path.

The legacy `/dance-sell` page also calls the same `IDanceSellPhase2Service.GetAsync` from `ReloadAsync` and `OpenJobAsync`. No alternate repository or ID transformation was found.

Relevant source locations:

- Route: `Components/Pages/RDanceJobDetail.razor:1`
- Lifecycle: `Components/Pages/RDanceJobDetail.razor:1010-1029`
- Load flow and fallback: `Components/Pages/RDanceJobDetail.razor:1031-1106`
- Service `GetAsync`: `Services/DanceSell/DanceSellPhase2Services.cs:2079-2080`
- Ownership lookup: `Services/DanceSell/DanceSellPhase2Services.cs:2114-2123`
- Repository lookup: `Services/DanceSell/DanceSellRepository.cs:192-196`
- Repository projection: `Services/DanceSell/DanceSellRepository.cs:847-889`
- Legacy load/open flow: `Components/Pages/DanceSell.razor:277-288` and `436-459`

## 2. Exact Blocking Condition

### Record missing

`DanceSellRepository.GetByIdAsync` returns `null` only when the query finds no row matching the supplied GUID. The service then throws:

```text
DANCE_SELL_NOT_FOUND
```

The repository query is:

```sql
SELECT ...
  FROM dance_sell.dance_sell_jobs
 WHERE id=@id;
```

It does not filter by status.

### Authorization failure

When a row is found, access is allowed only when all of the following are true:

```text
user.IsAuthenticated
AND (
    user.IsRoot
    OR user.Role is Admin/SystemOperator
    OR user.CustomerId == job.CustomerId
    OR user.UserId == job.UserId
)
```

If this evaluates to false, the service throws:

```text
DANCE_SELL_UNAUTHORIZED
```

### Tenant filtering

`DanceSellJobDto.TenantId` is selected, but `GetByIdAsync` does not call `TenantContext.EnsureLoadedAsync` and does not add `tenant_id=@tenant` to the `WHERE` clause.

Therefore, this read path has no direct tenant predicate. Tenant information is used by create/update paths, but not by the RDance detail lookup itself. `DanceSellSecurity.CanAccess` also has no tenant comparison.

### UI error masking

`RDanceJobDetail.ReloadAsync` catches one broad `Exception` around the entire job/render/reference/estimate/auto-finish load block:

```text
if ex.Message == DANCE_SELL_UNAUTHORIZED:
    "Bạn không có quyền xem video này."
else:
    "Không tìm thấy video quảng cáo thời trang."
```

Consequently, the visible “not found” message does not prove that the database row is missing. It is also shown for:

- database connection/query errors;
- `RenderJobs.GetAsync` errors;
- `ListReferenceVersionsAsync` errors;
- estimate-loading errors;
- other exceptions thrown after `DanceSell.GetAsync` succeeds.

## 3. Whether Old Jobs Are Automatically Re-entered

For an old job whose GUID is supplied to `/jobs/rdance/{id}`, the current route does attempt to load it through the same `GetAsync` path. There is no deployment-date gate or “new jobs only” condition in the route.

However, automatic re-entry is conditional:

- If the row is found and authorized, `ReloadAsync` continues into reference loading, estimate refresh and `ContinueAutoFinishAsync`.
- If the row is missing, unauthorized, or any later load operation throws, `_job` is cleared and the page displays the error.
- `OnParametersSetAsync` can then call `AutoPrepareReferenceAsync` only when `ShouldResumeAutoReference` is true and `_job` remains available.

Thus old jobs are not automatically recovered when the load path is blocked or when a later exception is masked as “not found”.

## 4. Whether Ready/Approved References Reach ContinueAutoFinishAsync

Yes, when the initial job load and subsequent reference-version load succeed.

`ReloadAsync` explicitly calls `ContinueAutoFinishAsync` at `RDanceJobDetail.razor:1092`.

`ContinueAutoFinishAsync` then:

- returns immediately when auto-finish is disabled, busy, active, terminal, or the render job is failed/cancelled;
- for `PreparedReferenceStatus == Ready` and latest reference `Status == Ready`, calls `ApproveCharacterAsync` or `References.ApproveAsync`;
- reloads and recursively re-enters `ContinueAutoFinishAsync`.

For an already `Approved` reference, the `Ready` approval branch is skipped. The method itself then returns; queueing is performed by the explicit user action path in `ConfirmAndQueueAsync`, which calls `QueueRenderFromUserActionAsync` after approval is observed.

Therefore:

```text
Ready reference:
  ReloadAsync -> ContinueAutoFinishAsync -> approve -> ReloadAsync -> ContinueAutoFinishAsync

Approved reference:
  ReloadAsync -> ContinueAutoFinishAsync -> returns
  QueueRenderAsync is not reached by ContinueAutoFinishAsync alone
  QueueRenderAsync can be reached by ConfirmAndQueueAsync/user action
```

## 5. Whether Generating Has a Guaranteed Re-entry Path

There is a polling path, but it is not a guaranteed re-entry path for every failure mode.

When `ShouldResumeAutoReference` is true, `PollLoopAsync` invokes `AutoPrepareReferenceAsync` on each timer tick:

```text
PollLoopAsync
  -> AutoPrepareReferenceAsync
     -> ReloadAsync
     -> References.AutoPrepareAsync when reference is generating
     -> ContinueAutoFinishAsync
```

The detail page also calls `AutoPrepareReferenceAsync` from `OnParametersSetAsync` when the same resume condition is true.

Limitations:

- `ReloadAsync` can clear `_job` and stop the chain on any exception.
- The generating branch depends on `ShouldResumeAutoReference`; it is not unconditional for every `Generating` row.
- If the reference completion changes state in the database but the resume predicate is false, this code does not guarantee that `ContinueAutoFinishAsync` is invoked solely because the state changed.
- No provider callback directly targeting the Blazor component was found in this scope; the page relies on reload/poll/re-entry.

Conclusion: `Generating` has a polling-based re-entry mechanism, but not a guaranteed completion callback/re-entry guarantee independent of the page remaining active and the reload path succeeding.

## 6. Recommended Minimum Code Change

Minimum corrective change, without changing database schema, provider logic, render logic, billing or points:

1. Keep `GetByIdAsync` and ownership behavior unchanged unless a separate security decision explicitly approves adding tenant enforcement.
2. In `RDanceJobDetail.ReloadAsync`, classify errors by stable error code before applying the generic fallback:
   - `DANCE_SELL_NOT_FOUND` -> not-found message;
   - `DANCE_SELL_UNAUTHORIZED` -> unauthorized message;
   - database/RenderJobs/reference/estimate errors -> distinct load/diagnostic message and preserve the actual category in logs.
3. Narrow the `try/catch` around `DanceSell.GetAsync` so a successful job lookup cannot be converted into “not found” by a later render/reference/estimate failure.
4. For the generating reference state, retain or make explicit one reliable re-entry trigger after polling detects `Ready`, then invoke the existing `ContinueAutoFinishAsync`; do not create a second queue/render implementation.
5. Add a regression test covering:
   - missing row -> `DANCE_SELL_NOT_FOUND`;
   - existing unauthorized row -> `DANCE_SELL_UNAUTHORIZED`;
   - post-`GetAsync` load failure is not rendered as “not found”.

## Runtime Evidence

The target GUID was not conclusively classified against the live database in this audit. A read-only runtime query was not completed because the available local PostgreSQL CLI was absent and the ad hoc PowerShell provider load was incompatible with the installed PowerShell runtime.

Therefore, source evidence can establish the exact classification rules, but cannot prove whether `bdb30808-6098-48ab-a47c-fbd6fe29aaa7` currently exists, belongs to the signed-in user/customer, or has a tenant mismatch.

