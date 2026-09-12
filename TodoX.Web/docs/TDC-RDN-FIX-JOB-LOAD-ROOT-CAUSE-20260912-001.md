# TDC-RDN-FIX-JOB-LOAD-ROOT-CAUSE-20260912-001

## 1. Exact Root Cause

Status: CODE FIX ALREADY PRESENT BEFORE THIS CHECK.

File: `Services/DanceSell/DanceSellRepository.cs`

Method: `GetByIdAsync(Guid id, CancellationToken ct)` via shared `SelectSql`

Current fixed line: `SelectSql` maps `orientation AS CharacterOrientation`
at line 852. The same fixed projection also appears in create return
projections at lines 82 and 162.

Exception before the fix: PostgreSQL query failure caused by selecting a
column that does not exist on the runtime schema.

Message before the fix: the database error was caused by
`character_orientation AS CharacterOrientation`.

Trigger: opening `/jobs/rdance/bdb30808-6098-48ab-a47c-fbd6fe29aaa7`
called `DanceSell.GetAsync()`, which called `DanceSellRepository.GetByIdAsync()`.

Why it happened: older query projection read `character_orientation`, while
the current runtime write/update path and production schema use `orientation`.
The job row existed and ownership matched, but DTO mapping failed during the
primary repository query before post-load detail steps could run.

## 2. Before Call Graph

```text
/jobs/rdance/{JobId}
  -> RDanceJobDetail.OnParametersSetAsync()
     -> ReloadAsync()
        -> DanceSell.GetAsync(JobId, AuthState.CurrentUser)
           -> RequireOwnedJobAsync()
              -> DanceSellRepository.GetByIdAsync()
                 -> SelectSql + " WHERE id=@id;"
                 -> SELECT character_orientation AS CharacterOrientation
                 -> database exception
        -> primary job load fails
```

Before `TDC-RDN-FIX-JOB-LOAD-ERROR-MASKING-20260911-002`, this could be
surfaced like a not-found style detail error. Current branch separates
primary load errors from post-load refresh errors.

## 3. After Call Graph

```text
/jobs/rdance/{JobId}
  -> RDanceJobDetail.OnParametersSetAsync()
     -> ReloadAsync()
        -> ProviderCatalog.GetDefaultRouteAsync(reference image)
        -> ProviderCatalog.GetDefaultRouteAsync(motion video)
        -> DanceSell.GetAsync(JobId, AuthState.CurrentUser)
           -> RequireOwnedJobAsync()
              -> DanceSellRepository.GetByIdAsync()
                 -> SELECT orientation AS CharacterOrientation
                 -> DanceSellJobDto mapped successfully
        -> RenderJobs.GetAsync(renderJobId) only when RenderJobId has value
        -> DanceRepo.ListReferenceVersionsAsync(_job.Id)
        -> RefreshEstimateAsync()
        -> ContinueAutoFinishAsync()
        -> StartPolling()
```

## 4. Exact Data Used For Reproduction

Runtime verification: NOT PERFORMED. No production smoke test or database
query was run in this check. The following data is from the TDC request and
the prior diagnostic report.

Job ID: `bdb30808-6098-48ab-a47c-fbd6fe29aaa7`

User ID: `dae8e8b8-842a-43cd-b68d-c0a634cc15bc`

Customer ID: `2610a0bf-3308-4c33-9df1-6eaad2585b7c`

Tenant ID: `95f5db58-4233-4dfe-9747-5276105af12a`

Status: `draft`

Motion media: `624a5000-3d5a-4d63-8249-a2be1f2275b7`

Motion URL:
`https://dashboard.todox.vn/uploads/dance-sell/2610a0bf33084c339df16eaad2585b7c/202609/motion-upload-4b82e989ccbf4f1388883ba557646df8.mp4`

Duration: `10` from `request_json.durationSeconds`

Reference status: `approved`

Reference URL:
`https://dashboard.todox.vn/uploads/dance_sell_character/202609/ec3d17355a3f4584b1d6a5e081198536.jpg`

RenderJobId: `NULL`

Motion render job ID: `NULL`

Request JSON:

```json
{
  "ratio": "9:16",
  "serviceId": "abcf33cd-6b69-4c8d-9daa-e84d92fc80ec",
  "autoFinish": true,
  "serviceCode": "FASHION_VIDEO",
  "durationSeconds": 10
}
```

## 5. Why Previous Fixes Did Not Resolve It

TDC-012 focused on the new UI render path and explicit render entry behavior.
It did not address the repository DTO projection mismatch.

TDC-FINAL-HARDENING and TDC-FINALIZE-DURATION-GATE focused on duration,
manual render gating, pricing identity, and render readiness. Those changes
preserved `durationSeconds = 10` but did not fix the primary SQL projection
that loads the job.

TDC-FIX-JOB-LOAD-ERROR-MASKING separated primary job load errors from
post-load refresh failures and preserved `_job` after refresh failures. That
made the symptom more diagnosable but did not by itself fix the
`character_orientation` projection mismatch.

The actual load failure was resolved when `DanceSellRepository.SelectSql` and
related projections were changed to use `orientation AS CharacterOrientation`.

## 6. Exact Files Changed

Files changed by the existing fix in branch history:

- `Services/DanceSell/DanceSellRepository.cs`
- `Tests/RDanceCustomerStatusAndPointsRegressionTests.cs`
- `..\TodoX.Web.Tests\DanceSellRepositoryTests.cs`
- `docs/TDC-RDN-JOB-LOAD-DEEP-DIAGNOSTIC-20260912-001.md`

File added during this check:

- `docs/TDC-RDN-FIX-JOB-LOAD-ROOT-CAUSE-20260912-001.md`

## 7. Exact Methods Changed

Existing fix:

- `DanceSellRepository.CreateDraftAsync()` return projection
- `DanceSellRepository.CreateAsync()` return projection
- `DanceSellRepository.GetByIdAsync()` through shared `SelectSql`
- `DanceSellRepository.ListAsync()` through shared `SelectSql`
- `DanceSellRepository.GetByRenderJobIdAsync()` through shared `SelectSql`
- `DanceSellRepository.GetByProviderTaskIdAsync()` through shared `SelectSql`
- `RDanceJobDetail.ReloadAsync()` primary-load vs post-load error handling
- `RDanceJobDetail.ClassifyPrimaryJobLoadError()`

No RDance production code was changed during this check.

## 8. Tests

Command:

```text
dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter RDanceCustomerStatusAndPointsRegressionTests
```

Result:

```text
PASS - Failed: 0, Passed: 28, Skipped: 0, Total: 28
```

## 9. Build

Command:

```text
dotnet build TodoX.Web.csproj -c Release --no-restore
```

Result:

```text
PASS - 45 warnings, 0 errors
```

Warnings are generated Razor nullable warnings in `AiProviders_razor.g.cs`.

## 10. Publish

Command:

```text
dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard
```

Result:

```text
PASS
```

Output:

```text
D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\TodoX.Web\artifacts\publish\todox-dashboard
```

## 11. git diff --check

Command:

```text
git diff --check
```

Result:

```text
PASS
```

## 12. Protected Areas

Not changed:

- Database schema
- Migrations
- Provider contract
- 79AI
- KIE
- Render worker
- Pricing
- Billing
- Points
- Token wallet
- RVideo
- Timelapse
- Upload contract
- TikTok contract
- Public API
- RDance UI layout/CSS

## 13. Runtime Verification

RUNTIME VERIFICATION: NOT PERFORMED

No production smoke test was run. The owner will test runtime.

## 14. Commit SHA

Existing pushed code fix:

```text
72d61df42e99a049e1103636dab925ef389ae3eb
fix(rdance): separate job load errors from detail refresh
```

This commit is present in both local `HEAD` history and
`origin/feature/rdn-onepage-ui-revamp`.

Additional later pushed commit containing the deeper repository fix/report:

```text
30e8e9a
```

Current local branch also contains an unrelated ahead commit:

```text
52af649 docs(rvideo): record 79ai fallback hardening
```

That commit was not created for this RDance task.

## 15. Push Result

The RDance job-load fix commits are already present on
`origin/feature/rdn-onepage-ui-revamp`.

This check did not push because the current local branch is ahead of origin
by an unrelated RVideo documentation commit, and pushing now would include
that unrelated commit together with this report.

## 16. Working Tree

Expected after this check:

- New report file: `docs/TDC-RDN-FIX-JOB-LOAD-ROOT-CAUSE-20260912-001.md`
- Pre-existing untracked file preserved:
  `docs/TDC-RDN-JOB-NOT-FOUND-AUDIT-20260911-001.md`
- Publish artifacts are ignored and must not be committed.

## Final Answer

Why job `bdb30808-6098-48ab-a47c-fbd6fe29aaa7` existed with correct
owner/customer/tenant data but RDance New UI still showed the generic load
message:

The job existed and authorization was valid, but the primary repository query
failed while mapping the row because code selected the old/non-runtime
`character_orientation` column for `DanceSellJobDto.CharacterOrientation`.
The runtime schema and write path use `orientation`. Once the query failed,
RDance detail could not obtain `_job`, so post-load steps such as render job
lookup, reference versions, estimate refresh, and auto-finish were not the
root cause.

The fixed branch maps `orientation AS CharacterOrientation`, preserves nullable
`RenderJobId`, keeps approved reference data valid, and keeps explicit
`Tạo video` as the render entry point for the draft job state.
