# TDC-RDN-FIX-JOB-LOAD-ERROR-MASKING-20260911-002

## 1. Scope

Fixed RDance New UI detail load error handling only.

Allowed and changed:

- `Components/Pages/RDanceJobDetail.razor`
- `Tests/RDanceCustomerStatusAndPointsRegressionTests.cs`
- `docs/TDC-RDN-FIX-JOB-LOAD-ERROR-MASKING-20260911-002.md`

## 2. Root cause

`RDanceJobDetail.ReloadAsync()` used one broad `catch (Exception)` for the whole load chain:

```text
DanceSell.GetAsync()
-> RenderJobs.GetAsync()
-> DanceRepo.ListReferenceVersionsAsync()
-> RefreshEstimateAsync()
-> ContinueAutoFinishAsync()
```

Any exception after a successful `DanceSell.GetAsync()` could clear `_job` and map to the false user-facing not-found message.

## 3. Before flow

```text
ReloadAsync()
  -> DanceSell.GetAsync()
  -> post-load refresh steps
  -> catch any Exception
       -> _job = null
       -> DANCE_SELL_UNAUTHORIZED => unauthorized message
       -> everything else => not-found message
```

## 4. After flow

```text
ReloadAsync()
  -> DanceSell.GetAsync()
       -> primary load catch only
       -> DANCE_SELL_NOT_FOUND => not-found message
       -> DANCE_SELL_UNAUTHORIZED => unauthorized message
       -> other primary load exception => detail refresh/load message
  -> post-load refresh steps
       -> post-load catch
       -> keep _job
       -> show detail refresh/load message
       -> log original exception
```

The successful business flow order is unchanged.

## 5. Error classification

NOT_FOUND:

- Only mapped from `DANCE_SELL_NOT_FOUND` thrown by the primary `DanceSell.GetAsync()` path.

UNAUTHORIZED:

- Still mapped from `DANCE_SELL_UNAUTHORIZED` thrown by the primary `DanceSell.GetAsync()` path.

POST_LOAD_ERROR:

- Covers failures from `RenderJobs.GetAsync()`, `ListReferenceVersionsAsync()`, `RefreshEstimateAsync()` and `ContinueAutoFinishAsync()`.
- `_job` is not cleared.
- The UI no longer displays the not-found message for these errors.
- Original exception is logged through the component logger.

## 6. Changed files

- `Components/Pages/RDanceJobDetail.razor`
- `Tests/RDanceCustomerStatusAndPointsRegressionTests.cs`
- `docs/TDC-RDN-FIX-JOB-LOAD-ERROR-MASKING-20260911-002.md`

## 7. Regression tests

Command:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter RDanceCustomerStatusAndPointsRegressionTests
```

Result:

```text
PASS - Failed: 0, Passed: 26, Skipped: 0, Total: 26
```

Coverage added:

- primary `DANCE_SELL_NOT_FOUND` maps only to not-found.
- primary `DANCE_SELL_UNAUTHORIZED` maps only to unauthorized.
- post-load refresh errors keep `_job` and do not map to not-found.

## 8. Build

Command:

```text
dotnet build TodoX.Web.csproj -c Release --no-restore
```

Result:

```text
PASS - 0 errors
```

Notes:

- Existing generated Razor nullable warnings remain.

## 9. Publish

Command:

```text
dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard
```

Result:

```text
PASS
```

Output directory:

```text
D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\TodoX.Web\artifacts\publish\todox-dashboard
```

## 10. git diff --check

Command:

```text
git diff --check
```

Result:

```text
PASS
```

## 11. Protected areas

Untouched:

- RDance render orchestration
- AutoFinish state machine
- reference generation
- reference approval
- 79AI
- KIE
- image upload
- video upload
- provider payload
- duration
- pricing
- billing
- points
- token wallet
- database schema
- migrations
- API contract
- RVideo
- Timelapse
- Create flow
- UI layout/CSS

## 12. Runtime verification

RUNTIME VERIFICATION: NOT PERFORMED

No production smoke test was run. No production database query or update was performed for `bdb30808-6098-48ab-a47c-fbd6fe29aaa7`.

## 13. Commit

Included in the final pushed commit for this task. The immutable commit SHA is reported after push.

Commit message:

```text
fix(rdance): separate job load errors from detail refresh
```

## 14. Push

Pending at report update. Target:

```text
origin/feature/rdn-onepage-ui-revamp
```
