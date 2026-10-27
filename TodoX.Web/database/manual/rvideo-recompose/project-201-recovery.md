# Project 201: conditional recovery and staging prerequisites

Production job reference: `464639f5-16ee-4953-80c1-2050fcadb3fb`. Project: `201`. This document is an operator procedure, not an assertion that its media or worker is ready. No production SQL/file writes, flag enablement or recovery job were executed by this DEV task.

## What the supplied read-only evidence establishes

The operator reports Completed, an existing final URL, selected_final_video_version_id=NULL, nine selected scene pointers, nine completed/is_selected=true versions, exactly one active features.final_video_versioning row with value false, required columns/unique indexes, validated project-selected-final and three item FKs, and one validated final-to-project FK. This is supplied operator evidence, not an independent query from DEV. It identifies a false negative in the previous `>=2` final FK count; it does not verify every FK's endpoints/key columns, local media bytes, source lineage or worker execution context.

The checked-in DDL declares TWO final FKs: project_id -> video_projects.id and nullable render_job_id -> render.render_jobs.id. The previous readiness implementation assumed both by count. Final version creation locks the project, takes exact selected scene pointers, creates queued version/items and stores the existing claimed worker job ID; publication updates version selection/project pointer in one transaction. None of these operations depends on the optional render-job FK or its ON DELETE behavior. No delete/cleanup is done as part of manual recompose. Missing that optional constraint may be a separately reviewed integrity concern; do not add it merely to satisfy a count.

The corrected gate requires these exact validated relations, independent of constraint names:

| Source | Target |
|---|---|
| video_projects.selected_final_video_version_id | final_video_versions.id |
| final_video_versions.project_id | video_projects.id |
| final_video_version_items.final_video_version_id | final_video_versions.id |
| final_video_version_items.scene_id | video_project_scenes.id |
| final_video_version_items.scene_video_version_id | scene_video_versions.id |

It matches source/referenced relation, ordered source/referenced attribute numbers, FOREIGN KEY type and validated state. Unrelated FKs or wrong columns cannot satisfy a requirement. `verify.sql` additionally reports whether the optional render-job FK is present/validated. No FK/migration/backfill is created or modified.

## Gate 1 — operator read-only verification

Use an existing production READ ONLY pg_service/role in DBeaver or psql; do not use production for integration tests. Save evidence outside application/media storage. From repository root:

```powershell
psql 'service=YOUR_EXISTING_READONLY_SERVICE' -X -f TodoX.Web/database/manual/rvideo-recompose/preflight.sql -o C:/Evidence/rvideo-preflight.txt
psql 'service=YOUR_EXISTING_READONLY_SERVICE' -X -f TodoX.Web/database/manual/rvideo-recompose/verify.sql -o C:/Evidence/rvideo-verify.txt
# Replace the tenant parameter with the actual read-only project tenant. Never guess it.
psql 'service=YOUR_EXISTING_READONLY_SERVICE' -X -qAt -v project_id=201 -v tenant_id=TENANT_UUID_FROM_READONLY_PROJECT -v job_uuid=464639f5-16ee-4953-80c1-2050fcadb3fb -f TodoX.Web/database/manual/rvideo-recompose/project-manifest.sql -o C:/Evidence/rvideo-201-selected-scenes.json
```

preflight/verify are READ ONLY transactions ending in ROLLBACK. project-manifest is one SELECT, strictly scoped by project, tenant and core job UUID. No output means identity mismatch/no matching project: STOP rather than removing predicates or selecting substitute versions. The manifest exposes the exact pointer, source scene/project/tenant lineage, status, is_selected, configured duration and existing source path/storage/media identity for every scene. Treat it as sensitive operational evidence (paths/public URLs); restrict access and do not commit it.

Review schema_ready=true, the exact five validated FK relations, compatible column types/check constraints/indexes and no relevant consistency anomalies. Optional render-job FK=false is informational. Global verify also reports unrelated projects; investigate relevant anomalies and assess global impact before enabling the GLOBAL flag. The flag must remain false at this stage.

For project 201, verify nine ordered scene entries, nine DISTINCT selected version IDs, completed/is_selected=true, lineageValid=true, correct tenant/project/scene IDs, and the sum of scene durations equals configuredTotalSeconds. Capture current final URL/path, source file hashes and NULL selected-final pointer. NULL is supported: it is not permission to mark an unverified legacy file Completed. No existing scene version may be changed or automatically selected.

## Gate 2 — media and actual worker preflight (NOT verified on DEV for project 201)

Perform on the instance/account that will actually claim the merge job:

1. Resolve each manifest version to the SAME existing bytes. The application tries existing SourceFilePath, configured local upload-root/object-key paths, durable MediaId/ObjectKey lookup, then recovery of existing stored media. It does not call a provider. Do not point a scene to a convenient different MP4 or generate replacement media. If a file exists only on another instance/object storage, confirm durable identity and approved localization before calling it ready. Record any localized path alongside the original selected version ID/storage key/hash; keep the original manifest unchanged.
2. The current reader finds the selected/completed flag for a scene; the queued final snapshot binds the exact selected_video_version_id with matching scene/project/tenant. Worker input IDs must match those persisted items; a pointer/flag inconsistency or selection race fails instead of silently picking a different version. Recheck the nine IDs immediately before the approved action.
3. Using the existing `Collect-RVideoFinalEvidence.ps1`, collect streams, packet PTS/DTS, decoded frame timestamps, hashes/freezes and strict decode logs for all nine EXACT selected inputs and the old final. It accepts the manifest `items.videoPath` shape exported above. If those paths are stale, STOP and have the operator establish the canonical resolver mapping; do not treat missing files as a successful preflight. Check video dimensions/FPS/codec/timebase and audio start/duration; mixed streams must go through the normalized path. Input duration shorter than configured needs explicit review, since padding can preserve timeline duration while repeating picture.
4. Confirm configured FFmpeg and adjacent ffprobe execute under the worker identity; `-version` must return 0 and `-encoders` must include libx264 and AAC. Confirm VideoRenderMergeHandler registration and actual merge worker claim settings/concurrency, configured MergeTimeoutMinutes, storage/public path mapping, no mock handler path, sufficient disk and worker read access to every source. A successful interactive Administrator probe does not prove service-account access. Confirm output-directory write/rename and IIS read access using the approved operational permission check; this DEV task performs no production write probe.
5. Confirm the package's DLL embedded BuildCommit and branch match the delivered commit/provenance file; do not deploy a stale baseline DLL. Confirm logs/events can be retained for input-probes, composition, concat, copy/normalized/fallback, final validation and any retries.

The prior diagnostic script can be invoked with the existing old final path; the output evidence directory must be NEW and separate. No file or worker preflight for project 201 has run here. Production freeze root cause and actual recompose readiness remain unconfirmed.

## Gate 3 — independent approved PostgreSQL staging

The user confirmed no approved PostgreSQL staging runner currently exists. Never use the production database open in DBeaver as the target of a write test. Provision an independent database/server or isolated test database via the owner's approved process, with verified host/database/user, no production endpoints/credentials and no production workers/providers. Use the existing reviewed schema through a separate approved provisioning process; no schema creation/migration is performed by this patch.

Prepare a dedicated disposable fixture, not real project 201: Completed NATIVE project with a legacy final URL/path, NULL selected final version, exact selected completed scene versions from the same tenant/project/scene, valid local video/audio fixtures under an approved fixture root, existing tenant/user/customer FK rows and all required constraints/indexes. Keep the optional render-job FK ABSENT in one approved fixture schema to verify the reported topology, without dropping constraints on a shared database. A schema variant with that optional FK present should also pass. Keep the project-selected/item/project FKs intact. Snapshot/backup the isolated test database and stop all workers/other clients for the harness.

`RVideoRecomposePostgresTests.cs` uses EXACTLY these environment variables (values must be supplied securely by the approved runner, not committed or pasted in chat):

- `TODOX_RVIDEO_PG_APPROVED=YES` — explicit opt-in; absent means SKIPPED before any connection.
- `TODOX_RVIDEO_WORKERS_STOPPED=YES`.
- `TODOX_RVIDEO_PG_CONNECTION` — isolated test connection from the runner's secret store.
- `TODOX_RVIDEO_PG_DATABASE`, `TODOX_RVIDEO_PG_HOST`, `TODOX_RVIDEO_PG_PORT`, `TODOX_RVIDEO_PG_USER` — exact independently verified allowlist; database name includes test, excludes prod. A name alone does not prove isolation.
- `TODOX_RVIDEO_PROJECT`, `TODOX_RVIDEO_TENANT`, `TODOX_RVIDEO_OWNER` — disposable fixture IDs, NOT production project 201.
- `TODOX_RVIDEO_MEDIA_ROOT` — fixture source path root.
- `TODOX_TEST_FFMPEG` — local executable, adjacent ffprobe; match the planned server build.

There is no tracked CI workflow or runsettings configuring these variables. Tracked launch configs are the Web/Landing application profiles, not a staging test runner; the local Web profiles set only ASPNETCORE_ENVIRONMENT. Current process has no TODOX_RVIDEO_* variables. No secret or connection-string values were printed during inspection.

From repository root, AFTER runner/fixture approval:

```powershell
dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj --filter 'FullyQualifiedName~RVideoRecomposePostgresTests' -v normal
# Then all focused local/media/UI/lineage checks, with TODOX_TEST_FFMPEG set:
dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj --filter 'FullyQualifiedName~RVideoFinal|FullyQualifiedName~RVideoManualRecompose|FullyQualifiedName~RVideoRecompose|FullyQualifiedName~VideoMergeCrossInstance' -v normal
```

The harness uses real job/version transactions, actual local FFmpeg validation before publishing a new completed test version, concurrent enqueue, terminal failure/retry, queued-version idempotency and pointer/item checks. Lifecycle/settings ports are isolated; the full worker and billing ledger still require the separate staging acceptance exercise from rollback.md. Its finally block restores only the approved fixture pointers and removes created test job/version/item rows; retained local evidence stays in a temporary directory. If cleanup/connectivity fails, quarantine the disposable fixture and restore its snapshot. Do not weaken approval/identity guards to force a PASS. Record actual schema-ready results in both optional-FK variants; current DEV results do not prove either real SQL topology.

## Gate 4 — conditional feature enablement and rollback

Only AFTER Gates 1–3 pass and the operator approves GLOBAL tenant impact, package rollout and media/worker access:

1. Record the unique settings row ID, original setting_value=false, is_active=true and backup/snapshot reference. Record the project pointer/URL/path and selected scene IDs again. Assess other tenants because this flag is global, not scoped to project 201.
2. Deploy the reviewed package through the normal separately approved IIS process; validate embedded BuildCommit/health and retained logs. No deployment was performed in this task.
3. Use the established managed-settings process to change ONLY the existing unique active features.final_video_versioning value to true. Do not insert a duplicate row, activate another row, change other versioning flags, create a FK or run a legacy backfill. No executable enable.sql is supplied while staging/media/worker gates are unverified; this document specifies the conditional operation only.
4. Re-run read-only preflight/verify: schema_ready=true, exactly one active true row, optional-FK audit informational. Confirm no unexpected final/scene/core-job changes just from enabling the flag or opening the UI. If other automatic lifecycle work is eligible, assess/control that separately before the global change.
5. For an explicitly approved recovery, use the existing manual UI action once. It must create only merge_video_job/FFmpeg work, no render_scene_video, provider task, TTS, AI usage or point charge. Monitor the one active merge, exact nine snapshot version IDs and durations, fallback reason, strict final-validation PASS and atomic final selection. Download/play the actual new bytes in browser and independent player; check the 54-second expected timeline against CURRENT configuration, all boundaries and NATIVE voice sync. Current operator evidence reported nine scenes; timeline/media checks must establish the actual configured result.
6. Verify old final file/hash remains intact and accessible at its original location, new final has a distinct version/output path, selected final ID/URL/path agree with the new completed version, scene selections/history are unchanged and no orphan items/duplicate active job remain. After a failure, old pointer/URL/path must remain unchanged; fix readiness/media issues and use the canonical retry, never rerender scenes as recovery.

Rollback: stop new manual requests and settle/cancel active merges through supported operations before restoring the recorded flag value false. Do not delete jobs/versions/scenes or blindly revert project pointers. Disabling the flag can make pending manual attempts fail safely at the worker readiness check. Restore the previous approved application package via the existing operator process if necessary; preserve all media/logs. No database schema rollback/backfill rollback is needed because this patch performs neither. A valid completed final is not automatically replaced with the old frozen file during application rollback. Any manual pointer repair requires a separate reviewed, media-validated operation.

**Project 201 is NOT declared ready for an actual merge.** The corrected FK gate removes a false schema blocker; the verified false flag still blocks manual enqueue. Actual staging PASS, exact lineage/identity evidence, local files, worker preflight, global-flag approval and rollout remain operator gates.
