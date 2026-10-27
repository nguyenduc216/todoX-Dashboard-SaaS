# RVideo recompose rollback and enablement gates

No production changes are made by these files. `preflight.sql` and `verify.sql` are READ ONLY and end with ROLLBACK. Run with an operator-managed read-only pg_service, never paste a connection string into source, command history or reports.

## Enablement

1. Stop and review if schema_ready is false. Inspect columns/types, index definitions (including the partial unique selected index), validated foreign keys and check constraints. Runtime readiness checks required columns, unique index key shapes/predicate and validated FK prerequisites; it does not prove the complete schema or media are correct. Catalog presence is not a substitute for data review.
2. Review verify.sql results. Legacy final without a selected final version is supported; missing selected COMPLETED scene versions, mismatched project/tenant pointers, orphan items and duplicate active merges require operator investigation. Do not auto-pick versions or render missing media.
3. Do not run the old `database/scene-media-versioning/02_backfill_existing_scene_media.sql` for this recovery: it marks existing files Completed without decoding them. This implementation does not require that backfill and does not modify that historical script.
4. Schema absent/incomplete: use the existing reviewed schema installation process only after separate approval. No new migration is included. Existing schema must be present before this feature is enabled.
5. Take the normal staging snapshot/backup, prepare an isolated disposable NATIVE project with selected completed scene fixtures, and run the real PostgreSQL integration test below. Review output files and browser audio/video playback. Verify no AI job, TTS, usage or point ledger changes.
6. Only after schema/data/staging verification and operator approval should the existing `features.final_video_versioning` global app setting be enabled through the managed settings process. There must be exactly ONE active row with value true/1/yes (case-insensitive; whitespace is not accepted). Other versioning flags are not changed here. The setting is GLOBAL in the current source, not tenant-scoped; evaluate impact across tenants before enabling it.
7. Re-run preflight/verify and one approved manual recompose. The new application creates a queued final version and exact ordered composition items; it does not fabricate a completed legacy version. Previous final URL/path are retained in the composition snapshot, the old file is never overwritten, and project selection changes only after validation and transaction commit.

`enable.sql` is intentionally NOT supplied: this DEV task has not verified an actual PostgreSQL schema or passed the real integration gate. The request permits supplying it only after those prerequisites are confirmed. Do not resolve the warning by blindly updating setting_value or removing the guard. Record the operator-approved flag change separately after preflight evidence is reviewed.

## Running read-only diagnostics

From repository root, using an already configured service alias:

```powershell
psql 'service=YOUR_EXISTING_READONLY_SERVICE' -X -f TodoX.Web/database/manual/rvideo-recompose/preflight.sql -o C:/Evidence/rvideo-recompose-preflight.txt
psql 'service=YOUR_EXISTING_READONLY_SERVICE' -X -f TodoX.Web/database/manual/rvideo-recompose/verify.sql -o C:/Evidence/rvideo-recompose-verify.txt
```

Use the preceding task's `scripts/diagnostics/rvideo-final-evidence.sql` and `Collect-RVideoFinalEvidence.ps1` for job `464639f5-16ee-4953-80c1-2050fcadb3fb`, on the production server only, after operator approval. No production data was accessed from DEV.

## Real PostgreSQL integration (operator-approved isolated staging only)

The integration test is skipped unless `TODOX_RVIDEO_PG_APPROVED=YES`. It never creates schema, migrates or enables flags. It performs data writes and cleanup only against the explicitly allowlisted disposable TEST database/project; never point it at production or a shared customer project. Stop all background workers and prevent other users from changing this fixture during the test. Endpoint/database/user must be independently verified by the test DB owner. The database name must include `test` and exclude `prod`; matching names alone are not proof of isolation.

Set environment variables securely in the test runner/session:

- `TODOX_RVIDEO_PG_APPROVED=YES`, `TODOX_RVIDEO_WORKERS_STOPPED=YES`.
- `TODOX_RVIDEO_PG_CONNECTION`: isolated test connection supplied by the secret store; do not print it.
- `TODOX_RVIDEO_PG_DATABASE`, `TODOX_RVIDEO_PG_HOST`, `TODOX_RVIDEO_PG_PORT`, `TODOX_RVIDEO_PG_USER`: exact approved endpoint/database/user allowlist.
- `TODOX_RVIDEO_PROJECT`, `TODOX_RVIDEO_TENANT`, `TODOX_RVIDEO_OWNER`: existing disposable Completed legacy project, final URL present, selected_final_video_version_id NULL, selected completed scene versions present, no active merge.
- `TODOX_RVIDEO_MEDIA_ROOT`: approved local fixture directory containing every selected scene SourceFilePath. The test rejects paths outside it.
- `TODOX_TEST_FFMPEG`: local executable; ffprobe alongside it. Use the server's FFmpeg build for staging validation.

```powershell
dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj --filter 'FullyQualifiedName~RVideoRecomposePostgresTests' -v normal
```

This test executes actual RenderJobService concurrency locks/inserts and SceneMediaVersioningService version/item/publication transactions on independent connections. Configuration/lifecycle event ports are isolated in the test; it does not run the whole worker or provider pipeline. It tests two concurrent manual requests, terminal failure then fresh identity, queued-version idempotency, exact scene item IDs, old legacy final retention on failure, actual local media merge/decode, transactional selected final consistency, unchanged scene selections, zero point costs and no provider task/AI usage for created jobs. Generated media/logs remain in the printed/inspectable temporary directory. Fixture pointers and created job/version/item rows are cleaned in finally; inspect and restore the disposable database snapshot if connectivity/cleanup fails. No original scene file is removed. Retain the test results and verify.sql output as release evidence; a skipped test does NOT satisfy the gate.

Separately exercise the real worker on staging: force FFmpeg failure before publication, verify the existing selected final ID/URL/path/file, then retry successfully; check scene jobs/TTS/provider usage and point ledger counts before/after. Verify actual lip sync by playback. Source contracts, tone timestamps and fake UI tests do not prove real speech/lip synchronization or the billing ledger boundary.

## Rollback

- Application: deploy the previously approved package using the existing operator process, preserve media/logs, stop new recompose requests first and let active merges finish or explicitly cancel them through the supported job mechanism. No automatic IIS actions are included.
- Flag: disabling the flag blocks new manual recompose. This worker also rechecks readiness, so pending manual attempts will fail safely if flag/schema are no longer ready; selected final and old file stay intact. Do not toggle while jobs are running without an operational plan. Record/restore the previous flag value through managed settings, not ad hoc SQL in this task.
- Data: do not delete final versions/items or reset selected scene versions. No backfill rollback is needed because none is performed. To recover an existing final version, use the established authorized final-version selection workflow after media validation. A legacy old final remains at its original physical path and its location is retained in the new version's composition snapshot; restore its pointer only through a separately reviewed operator procedure.
