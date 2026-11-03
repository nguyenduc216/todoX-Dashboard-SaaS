# Final versioning FK readiness correction and project 201

The previous readiness guard incorrectly inferred a required FK count of two from the original DDL: final_video_versions.project_id references video_projects and nullable render_job_id references render_jobs. Final composition/selection needs the project relationship; the optional render-job FK is not a prerequisite of the persistence operations. Counting arbitrary FKs also allowed unrelated relationships to satisfy checks.

The C# guard and operator verify.sql now match five exact validated FK relations by source table/ordered source keys and referenced table/ordered referenced keys: project selected final -> final version; final version project -> project; composition item -> final version, scene and scene video version. Constraint names/counts are not used. The optional final render-job FK is reported separately. No FK, migration, schema write, feature enablement or synthetic completed backfill is included.

Operator-supplied production read-only evidence for project 201 indicates a legacy Completed project with existing final URL, NULL selected final pointer, nine selected completed scene versions, one active false feature setting and the reported required schema. These facts were not independently queried from DEV. The corrected schema gate removes the reported false negative, while the false flag still correctly blocks manual recompose. Project 201 is NOT declared media/worker ready or production-ready.

## Changed files

- Services/VideoRender/FinalVideoVersioningState.cs: exact required FK endpoints/keys/validation, optional render-job FK excluded from prerequisite.
- database/manual/rvideo-recompose/verify.sql: identical schema predicate, optional FK audit.
- TodoX.Web.Tests/RVideoRecomposeCompatibilityTests.cs: regression for exact five relations, optional FK exclusion, SQL/runtime parity and existing selected snapshot/lineage contracts.
- Tests/TodoX.Web.Phase1B.Tests.csproj: include the existing eight local/cross-instance merge input recovery tests in the tracked test project.
- database/manual/rvideo-recompose/project-manifest.sql: read-only, parameterized project+tenant+core-job evidence for exact selected versions, lineage and media identities.
- database/manual/rvideo-recompose/project-201-recovery.md: full conditional recovery, independent staging setup, exact environment variable names, global-flag enablement/rollback and media/worker acceptance gates.
- database/manual/rvideo-recompose/rollback.md: clarify corrected FK contract and link the later recovery runbook.
- This report.

Legacy NULL selected-final remains supported by the existing flow: queue a NEW final version/items from exact current scene pointers; retain previous final location in snapshot and original URL/path/file until validated new publication commits. Creation checks selected pointer and scene/project/tenant lineage; worker compares resolved version IDs with persisted composition items and fails on disagreement. No alternate version selection or render is introduced. Source contracts and local resolution tests pass; actual production lineage/files and service-account access remain operator checks.

Frozen areas unchanged: image/video scene generation, provider payloads/adapters, TTS, pricing/points/billing, scene identity/history, RDance and Timelapse. No provider/TTS invocation or database connection occurred in this task. No DB update is required by this correction.

## Verification

Isolated worktree: artifacts/rvideo-readiness-review, based on 16a1bc6563d81f759d55aa0ed008625ba26c53e3. Unrelated existing main-workspace edits are preserved and excluded.

Regression first ran against the old source and failed: ReadinessChecksExactForeignKeyRelationsWithoutRequiringRenderJobFk (1 failed), demonstrating the old count-based guard. With the correction, from isolated root and TODOX_TEST_FFMPEG set to the existing local FFmpeg/adjacent ffprobe:

```powershell
dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj --filter 'FullyQualifiedName~RVideoFinal|FullyQualifiedName~RVideoManualRecompose|FullyQualifiedName~RVideoRecompose|FullyQualifiedName~VideoMergeCrossInstance' --no-restore -v quiet
```

46 PASS, 0 failed, 1 SKIPPED (real PostgreSQL integration), total 47. Includes real local FFmpeg fixtures, Blazor component execution, request unit tests, eight local/cross-instance media recovery tests and explicit source contracts. SQL FK/catalog expressions have NOT been executed on PostgreSQL. No fake SQL result is claimed as database verification.

The user confirms there is no approved PostgreSQL staging runner. Inspection found no tracked CI workflow/runsettings configuring it; tracked Web/Landing launch profiles are application launch configs. Local Web launch profiles contain only ASPNETCORE_ENVIRONMENT, no TODOX_RVIDEO keys. Current process has no TODOX_RVIDEO_* variables. Only variable names were inspected/reported, not secrets/connection-string values. The existing PostgreSQL fact remains SKIPPED before any connection. Exact required variables and independent disposable-fixture setup are listed in project-201-recovery.md. Missing gates: approved isolated endpoint/user/database and secret injection, fixture tenant/owner/project and selected local media, workers stopped, existing reviewed schema/flag readiness, actual integration PASS, real worker/billing checks and actual playback.

Lint: dotnet format whitespace verify for modified runtime/test files and git diff --check. Release build/publish use the isolated source with the designated repository-root artifacts/publish/todox-dashboard output. Exact commands:

```powershell
dotnet format TodoX.Web/TodoX.Web.csproj whitespace --verify-no-changes --no-restore --include TodoX.Web/Services/VideoRender/FinalVideoVersioningState.cs
dotnet format TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj whitespace --verify-no-changes --no-restore --include TodoX.Web.Tests/RVideoRecomposeCompatibilityTests.cs
dotnet build TodoX.Web/TodoX.Web.csproj -c Release --no-restore -v quiet
dotnet publish TodoX.Web/TodoX.Web.csproj -c Release -o D:/todoX/Dashboard-web/TodoXPortal/todoX-Dashboard-SaaS/artifacts/publish/todox-dashboard --no-restore -v quiet
```

Logs remain under ignored artifacts/rvideo-readiness-*.log. Build/publish outcomes and committed DLL BuildCommit/provenance/hash are verified and reported at delivery. Existing package/compiler warnings are retained; unrelated fixes are out of scope. Full suite is not repeated for this catalog-predicate correction; previous full-suite failures remain documented in the prior report.

## Operator boundary

Follow database/manual/rvideo-recompose/project-201-recovery.md. Do not enable features.final_video_versioning merely because schema_ready becomes true. First establish exact project 201 scene identities, media resolution/bytes and worker identity/permissions/tools; provision approved independent PostgreSQL staging and obtain real integration/worker acceptance. Only then use the managed global-settings process with explicit approval, recorded old value/backup and cross-tenant impact review. Re-run read-only checks after enablement and before one approved manual recovery. No executable enable.sql is supplied while those gates remain unverified. No production writes or IIS deployment were performed; do not merge main automatically.
