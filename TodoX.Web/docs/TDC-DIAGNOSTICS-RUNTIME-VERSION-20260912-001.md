# TDX-DIAGNOSTICS-RUNTIME-VERSION-AND-RDANCE-20260912-001

## 1. Root cause and current status

The branch already contained the `orientation AS CharacterOrientation` RDance repository fix, but the application had no reliable runtime marker or admin diagnostic page. The existing `/system/version` endpoint exposed only partial assembly metadata, and Git metadata was not automatically embedded when the build ran inside the repository.

This task adds:

- Git-derived build metadata embedded into the application artifact.
- A shared runtime build information provider.
- An admin-only diagnostics page at `/admin/system/diagnostics`.
- Read-only database and RDance schema diagnostics.
- An RDance job diagnostic that uses the production load path and preserves the real exception details for administrators.

Production is not declared fixed because no authorized production deployment or smoke test was available in this run.

## 2. Code inspected

- `TodoX.Web.csproj`
- `Program.cs`
- `Data/TodoXConnectionFactory.cs`
- `Services/TenantContext.cs`
- `Services/AdminEndpointAuthorization.cs`
- `Services/DanceSell/DanceSellRepository.cs`
- `Services/DanceSell/DanceSellPhase2Services.cs`
- `Components/Pages/RDanceJobDetail.razor`
- `Components/Layout/MainLayout.razor`
- Existing admin pages and regression tests

## 3. Files changed

- `TodoX.Web.csproj`
- `Program.cs`
- `Components/Layout/MainLayout.razor`
- `Components/Pages/AdminSystemDiagnostics.razor`
- `Services/SystemDiagnostics/RuntimeBuildInfo.cs`
- `Services/SystemDiagnostics/SystemDiagnosticsModels.cs`
- `Services/SystemDiagnostics/SystemDiagnosticsService.cs`
- `Tests/SystemDiagnosticsRegressionTests.cs`
- `Tests/RVideoRuntimeSqlTests.cs`

No database migration or schema file was changed.

## 4. BuildInfo implementation

`PopulateBuildMetadataFromGit` reads:

- `git rev-parse HEAD`
- `git rev-parse --abbrev-ref HEAD`
- the latest Git commit message

`GenerateBuildMetadataSource` embeds the values as assembly metadata before compilation. The artifact also includes build and publish timestamps. Fallback values remain available for environments without Git.

`RuntimeBuildInfoProvider` exposes:

- application and environment
- branch and full/short commit SHA
- commit message
- build and publish time
- assembly and informational version
- .NET runtime
- machine name
- process start time

`/system/version` now reads this provider, preserving its existing safe configuration exposure rules.

## 5. Diagnostics implementation

Added `ISystemDiagnosticsService` and an admin-only page:

`/admin/system/diagnostics`

The page includes:

- Application Runtime
- Database
- RDance Schema Compatibility
- Deployment Status
- Health Check
- RDance Job Diagnostic

The admin navigation includes a Build marker linking to the diagnostics page. Normal users are denied by both navigation rules and the page guard.

## 6. Database diagnostic

The service uses the existing `TodoXConnectionFactory` and `TenantContext`. It performs read-only checks:

- database connection
- PostgreSQL server/version
- current tenant
- `information_schema.columns`
- `dance_sell.dance_sell_jobs` columns:
  `id`, `tenant_id`, `customer_id`, `user_id`, `render_job_id`,
  `orientation`, `character_orientation`, `request_json`, `result_video_url`

Connection strings, passwords, tokens, and secrets are never displayed.

`character_orientation` is reported as a compatibility check, while `orientation` is the required runtime projection column.

## 7. RDance diagnostic

The admin job check calls:

`IDanceSellPhase2Service.GetAsync(jobId, currentUser, ct)`

This follows the same ownership and repository path as RDance Detail. It displays job identity, tenant/user/customer, status, render job, motion asset, reference status, prepared reference URL, and result URL.

On failure it preserves:

- exception type
- PostgreSQL SQL state when available
- message
- repository/method context
- inner exception
- full exception text/stack trace

It does not replace the exception with the user-facing not-found or incomplete-data message.

## 8. Tests

Focused command:

`dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter SystemDiagnostics`

Result: PASS, 6 passed, 0 failed.

The full existing test project was also run. It reported 10 pre-existing/out-of-scope failures in voice prompt, Timelapse, RDance reference prompt, and RVideo lifecycle regression tests. Those protected/unrelated areas were not modified.

## 9. Build

Command:

`dotnet build TodoX.Web.csproj -c Release --no-restore`

Result: PASS, 0 errors. Existing Razor nullable warnings from generated `AiProviders` code remain.

## 10. Publish

Command:

`dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`

Result: PASS.

Output:

`artifacts\publish\todox-dashboard`

Verified:

`artifacts\publish\todox-dashboard\TodoX.Web.dll`

The generated metadata source contains:

- BuildCommit: `6c558733c2830778b9d7230330d41b3c566259db`
- BuildBranch: `feature/rdn-onepage-ui-revamp`

Publish artifacts remain ignored and were not committed.

## 11. Deploy

No production deployment or IIS restart was performed. The task did not establish safe authorized deployment access, and repository instructions prohibit an unapproved deployment/restart.

Result: NOT PERFORMED.

## 12. Runtime smoke test

Required production checks:

- `/admin/system/diagnostics`
- running commit SHA
- build time
- environment
- database status
- RDance job `bdb30808-6098-48ab-a47c-fbd6fe29aaa7`

These checks could not be run against production from this workspace.

Result: `RUNTIME VERIFICATION BLOCKED`

Production is therefore not declared fixed.

## 13. Running production commit SHA

Unknown. No authorized production runtime endpoint or deployment session was available.

## 14. Expected commit SHA

`6c558733c2830778b9d7230330d41b3c566259db`

Branch:

`feature/rdn-onepage-ui-revamp`

## 15. Mismatch explanation

The production SHA cannot be compared because production runtime verification was blocked. The expected SHA is the new local commit containing the diagnostics implementation.

## 16. New commit SHA

`6c558733c2830778b9d7230330d41b3c566259db`

Commit message:

`feat(diagnostics): add runtime and rdance diagnostics`

## 17. Push result

PASS.

Code commit `6c558733c2830778b9d7230330d41b3c566259db` and report commit `7be4287` were pushed to:

`origin/feature/rdn-onepage-ui-revamp`

## 18. Working tree status

Publish output is ignored. The task files and report are committed. Final working tree status is clean after the follow-up report update.

## Protected areas untouched

- RDance render workflow
- 79AI and KIE provider contracts
- RVideo
- Timelapse
- billing, wallet, and points
- upload and TikTok contracts
- database schema and migrations
- deployment infrastructure
