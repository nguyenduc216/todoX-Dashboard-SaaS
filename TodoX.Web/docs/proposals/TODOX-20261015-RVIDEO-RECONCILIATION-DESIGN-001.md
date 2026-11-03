# Image enqueue UNKNOWN reconciliation — proposal only

CHANGE_ID: TODOX-20261015-RVIDEO-RECONCILIATION-DESIGN-001. No runtime, database, or production changes authorized. Frozen: provider payloads, video reconciliation, audio/voice, points/billing, existing migrations and UI.

## Evidence

- `../database/scene-media-versioning/01_add_scene_media_versioning.sql:12-63`: image versions have tenant/id, unique logical request, nullable render job ID, JSON config and pending_reconciliation status; no lease/deadline.
- `../database/V008_Render_Jobs_Queue_Ready.sql:10-65`: render jobs and events are separate tables.
- `Services/VideoRender/RVideoSceneImageRenderAction.cs:178-274,276-313`: version precedes enqueue; job input contains imageVersionId; exception triggers tenant-scoped probe then UNKNOWN, never inferred FAILED.
- `Services/Render/RenderJobService.cs:95-147`: job insertion precedes event insertion; `:348-444` scene-dedup is a separate enqueue path.
- `Services/VideoRender/SceneMediaVersioningService.cs:768-795`: probe uses tenant/type/version JSON and UNKNOWN update guards queued; `:685-721,508-580` provider progression can update status independently.
- `Services/Render/RenderJobWorker.cs:51-99`: tenant-scoped config-gated normal dispatcher; `Services/VideoRender/SceneVideoReconciliationWorker.cs:21-73` owns video only. `Components/Pages/RVideo/RVideoV2Preview.razor:444` surfaces action errors. `../code-history/20261007-2340_RVID-UI-V2-SCENE-RERENDER-HISTORY.1_scene-rerender-history.md:34-43` freezes adjacent contracts.

## Proposed design and decisions

FEATURE_ID: RVID-UI-V2-SCENE-RERENDER-HISTORY. This is DESIGN_READY for review, **not** approved for execution. Paths in evidence below are relative to `TodoX.Web` (database/history paths are relative to repository root). The deployed schema has not been inspected; DDL is conditional on DBA schema-drift preflight.

Prefer typed version columns `enqueue_reconcile_after`, `enqueue_reconcile_deadline`, `enqueue_reconcile_claim_token`, `enqueue_reconcile_lease_until`, `enqueue_reconcile_attempts`, `enqueue_reconcile_last_checked_at`, `enqueue_reconcile_manual_at`, `enqueue_reconcile_handed_off_at`, `enqueue_reconcile_alert_type`, `enqueue_reconcile_alert_next_at`, `enqueue_reconcile_alert_attempts`, `enqueue_reconcile_alert_sent_at`, `enqueue_reconcile_alert_ack_at` over JSON metadata: typed due/backlog indexes and atomic fencing. Nullable columns distinguish untouched rows from backfilled attempts=0. `manual_at` + alert type is a durable work item; alert delivery and admin acknowledgement are distinct. Existing `error_code` can change when the handler enters provider reconciliation, so a separate handoff marker is needed; existing `updated_at` cannot be a durable deadline or notification cursor. Keep identity as tenant + version id; job evidence requires same tenant, job type `render_scene_image` and matching input imageVersionId, projectId, sceneId and logicalRequestId against the version. JSON-only is brittle for indexed due scans and concurrent compare-and-swap. Do not presume uniqueness: count **all** jobs matching tenant/type/imageVersionId before validating remaining fields; duplicates or malformed/mismatched fields require manual review. No new job schema, provider contract, public API or billing operation proposed. Operational interval, lease, backoff, batch limit, deadline and alert destination must be configured and approved, never embedded as policy constants in SQL.

**CONFIRMED_NOT_ENQUEUED is not provable under the current contract.** Empty SELECT can race uncertain insert/commit, and deleted or malformed job evidence is possible. Missing job event does not disprove job persistence. Deadline means manual verification, never FAILED or automatic re-enqueue. A matching committed job is positive evidence only; do not choose arbitrarily from duplicates.

| Current status | Evidence | Proposed transition |
| --- | --- | --- |
| pending_reconciliation + `RENDER_JOB_ENQUEUE_UNKNOWN`, no provider task/result | exactly one validated `queued` job (unclaimed, not started, no worker/lock, eligible or scheduled retry) before deadline | atomically link ID, set `status='queued'`, clear UNKNOWN error, set handed_off_at; **positive persisted job + eligible queue status** proves ownership; existing job worker claims normally, no enqueue |
| pending_reconciliation + enqueue UNKNOWN | exactly one validated `preparing`/`rendering`/`post_processing` job | link ID and mark handed_off_at; if provider task/result already exists do not change status; otherwise transition to `processing` (not queued); handler remains owner |
| pending_reconciliation + enqueue UNKNOWN | exactly one validated terminal `completed`/`failed`/`cancelled` job | no status inference: if version already completed preserve; otherwise manual verification of version/result/events/billing, keep pending |
| pending_reconciliation + provider error code (`provider_pending` / `SCENE_IMAGE_POLL_TRANSIENT`) | any | provider retry lifecycle owns this state; enqueue reconciliation never claims |
| pending_reconciliation | no job / lookup error / ambiguous matches, before deadline | remain pending; bounded retry, alert on ambiguity |
| pending_reconciliation + enqueue UNKNOWN | deadline elapsed, ambiguous identity, terminal mismatch or impossible state | remain pending, persist manual_at + alert type atomically; no further automatic enqueue scans; alert delivery retries separately |
| submitted / processing / completed / failed / cancelled | any | unchanged; stale claim cannot overwrite provider progress |
| queued without UNKNOWN marker | any | unchanged, not eligible |

Evidence for the queued transition is **job row queued and unclaimed**, not an empty lookup. A job in `queued` with a claim, started_at, or unexpected retry state is ambiguous: manual verification, not a reset. For active jobs, setting processing only if the version is still enqueue UNKNOWN and has no provider progress gives the running handler room to write submitted/provider-pending/completed. Never move a provider-pending version to queued/processing. If handler wins the row lock, re-read status/error and leave it alone; if handoff wins, handler's existing `MarkSceneImageVersionSubmittedAsync`, `MarkSceneImagePendingReconciliationAsync` and `TryCompleteImageVersionAsync` continue normal completion. Existing handler may finish directly without a submitted stage. Job worker claim selects queued jobs only (`Services/Render/RenderJobService.cs:620-636`), dispatches (`Services/Render/RenderJobWorker.cs:93-99`); handler loads image version, submits/polls or completes (`Services/Render/SceneImageRenderWorkItemHandler.cs:53-113,181-207`). Existing UNKNOWN marker is guarded by queued (`Services/VideoRender/SceneMediaVersioningService.cs:785-795`); provider pending uses distinct error codes (`:712-723`). Do not treat existing `FindImageRenderJobAsync` (LIMIT 1, `:768-780`) as a unique-identity proof. Persist a successful handoff marker in the same transaction as the version transition to prevent reprocessing. No change to job worker, provider/billing or handler is included in this design task; future implementation must prove these transition guards with PostgreSQL tests. A status-write failure leaving queued UNKNOWN outside the scan requires operational log investigation.

## Proposed worker (pseudocode, not executable)

```text
if !RenderQueue.Enabled or !ImageEnqueueReconciliation.Enabled: return
for each tenant using verified hosted-service tenant ownership:
  begin transaction
    SELECT bounded due versions FOR UPDATE SKIP LOCKED WHERE tenant_id = tenant
      AND status='pending_reconciliation' AND error_code='RENDER_JOB_ENQUEUE_UNKNOWN'
      AND deadline IS NOT NULL AND after <= DB_now AND manual_at IS NULL
      AND handed_off_at IS NULL AND (lease_until IS NULL OR lease_until < DB_now)
    set fresh random claim_token, lease_until = DB_now + configured lease
  commit
  for each claim:
    query at most two jobs for tenant + job_type + imageVersionId (count duplicates first)
    begin transaction
      lock version FOR UPDATE
      if token differs or lease expired or status/error_code changed: rollback/no-op
      else if DB_now >= deadline or duplicate/mismatch/terminal: persist manual_at,
        alert_type, alert_next_at=DB_now; clear claim (do not remove work item)
      else if exactly one valid job: re-read job status/claim under row lock;
        link ID + handoff timestamp; queued unclaimed -> version queued;
        active -> version processing only if no provider progress; else preserve;
        clear claim in same transaction
      else: increment attempts, set last_checked_at, schedule after capped at deadline;
        clear claim (never enqueue or fail)
    commit
independent tenant-scoped alert scan: SELECT manual_at IS NOT NULL AND
  alert_ack_at IS NULL AND alert_next_at <= DB_now, even after worker restart;
  notify with idempotency key (tenant_id, version_id, alert_type);
  on failure increment alert_attempts, schedule retry; on success set sent_at;
  keep manual row queryable until authorized admin records ack_at
```

Expired leases permit restart/new claimant but stale token cannot write. Re-read job under lock and version under fencing, with consistent lock ordering (version then job); never hold a lock across provider/notification calls. Check deadline before positive recovery; deadline reached routes manual. Lookup exceptions must release/expire claim and retry only within deadline. A second job inserted after counting is still possible under today's non-unique JSON identity: do not claim DB-enforced uniqueness; require an approved serialization/uniqueness contract before enabling automatic positive handoff (e.g. reviewed unique partial job identity index with duplicate cleanup, or job-writer advisory lock plus reconciliation lock). Until that gate passes, positive matches route manual, not unsafe automatic handoff. Do not derive cross-tenant enumeration from request-scoped HTTP tenant context without explicit verification. Alert sends may repeat after successful delivery but before sent_at commit: idempotency key is required at the delivery boundary; if unavailable, admin backlog remains authoritative and duplicates must be tolerated.

## Rollout, backfill and rollback

1. DBA read-only preflight: inspect live constraints/columns/indexes and permissions; count UNKNOWN rows by tenant/error code, existing links, and duplicate job JSON identities; inspect schema drift and test DB ownership. Approve tenant enumeration and alert owner.
2. Review additive forward SQL draft. Phase 1 short transactional ADD COLUMN and NOT VALID constraints; commit promptly. Phase 2 CREATE INDEX CONCURRENTLY **outside any transaction block**, with separate preflight and invalid-index cleanup. DDL may wait for locks: use approved session lock_timeout and statement_timeout and retry at low traffic; ALTER TABLE requires ACCESS EXCLUSIVE until commit, concurrent index avoids sustained write blocking but takes longer and can fail leaving an invalid index. Validate constraints separately during an approved window. Never wrap both phases in one transaction. Columns with existing names/type/constraints that differ mean STOP for schema drift, not silent IF NOT EXISTS. An existing invalid index means DBA DROP INDEX CONCURRENTLY outside transaction after inspecting it, then rerun phase 2. PostgreSQL concurrent index cannot run inside BEGIN or a transaction-wrapping migration runner.
3. Backfill **only** `pending_reconciliation` + `RENDER_JOB_ENQUEUE_UNKNOWN` using a DBA-approved operational window parameter measured from backfill time (not mutable updated_at), setting after and deadline in a separately reviewed operator procedure; review counts and samples before enabling. New UNKNOWN writes need a future runtime persistence path to set deadlines atomically or a bounded sweeper to initialize them, with an alert for uninitialized rows; draft DDL alone does not do so. Do not enable worker until that contract, unique-identity serialization and tenant ownership are verified. Canary gated worker after PostgreSQL concurrency tests, then monitor backlog, stale leases, ambiguous identities, uninitialized rows and manual alerts.
4. Disable worker before rollback. Preflight inventory **all** metadata; archive with tenant/id and verified row counts/checksum in DBA-controlled storage; separately verify archive readability and retention; get explicit DBA approval; only then execute a separately reviewed destructive rollback and postcheck. The draft fails closed whenever any meaningful metadata exists, including backfilled attempts=0; archiving alone does not bypass its guard. DBA must explicitly authorize and review an evidence-clearing step before rollback. Do not erase version status or existing render_job_id.

Manual backlog query for future tenant-scoped admin tooling (not executed): `SELECT id, tenant_id, project_id, scene_id, enqueue_reconcile_manual_at, enqueue_reconcile_alert_type, enqueue_reconcile_alert_sent_at, enqueue_reconcile_alert_ack_at FROM video_render.scene_image_versions WHERE tenant_id = @tenant AND enqueue_reconcile_manual_at IS NOT NULL AND enqueue_reconcile_alert_ack_at IS NULL ORDER BY enqueue_reconcile_manual_at, id LIMIT @limit;`. Admin access must enforce tenant authorization; `ack_at` is set only after human acknowledgement, never by notification success. Manual records remain discoverable after restart; delivery scans independently even if enqueue scan stops at deadline.

## Future runnable test matrix (not run)

Isolated PostgreSQL with real DDL, two independent connections and no provider calls in reconciliation: persist-job-then-event-throw; uncertain insert commit after initial empty lookup; missing job through deadline; two-worker SKIP LOCKED exclusivity; lease expiry with stale token rejected; crash/restart recovery; cross-tenant, project, scene, logical request and wrong job type rejection; duplicate/malformed JSON and late duplicate insert; queued eligible positive handoff → existing worker/handler → normal completion; active job → processing → handler completion; handler completes while handoff waits; provider_pending and transient poll error remain provider-owned; terminal job with missing result → manual; already-completed version unchanged; claimed queued job manual; backfill attempts=0 blocks rollback; notification failure/restart/resend/ack/backlog and idempotency; failed concurrent index/invalid index rerun, lock_timeout, old application compatibility, deadline boundary and lookup error. Assert no second enqueue, false FAILED, provider/billing contract changes or cross-tenant disclosure. PostgreSQL test infrastructure/ownership requires approval before implementation. **Runtime tests NOT RUN; PostgreSQL tests NOT RUN.**

Preflight: read-only catalog queries for actual deployed schema, column types/defaults, constraint definitions, index validity and PostgreSQL version; grouped duplicate `(tenant_id,input_json->>'imageVersionId')` image jobs; counts of matching UNKNOWN status/errors and render_job_id, row/table size and long transactions. Postchecks after each phase: catalog columns/constraints, `pg_index.indisvalid AND indisready` for both indexes, counts of unchanged jobs and unrelated version statuses; deadline/manual distributions after separately approved backfill. Approval gates: actual schema drift, DB-supported serialization/uniqueness of positive identity, eligible queued/active handoff, deadline/backoff/attempt policy, tenant iterator, alert/manual owner and delivery idempotency, backfill window, lock budget and destructive rollback. No migration executed or production tests run in this design task.