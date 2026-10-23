# Project 188 — exact seven-scene pre-HTTP reconciliation

**Current verification decision: HOLD.** Operator confirms the deployed DLL hash matches the application release and the new UI works. This does not settle the old pending jobs. See [the current verification findings and read-only server collection commands](project188-scenes831-837-VERIFICATION.md). No execution of reconciliation is directed while per-job evidence, schema sign-off and fleet fencing remain unapproved.

Bộ SQL đã khóa đúng 7 cặp UUID do operator cung cấp. Script **chưa được chạy** và **mặc định không cho thực thi repair** khi chưa có supplemental snapshot, schema review, fencing và phê duyệt no-submit cho từng operation. Operator đã báo thu thập đủ supplemental_manifest; dùng nguyên bản đã lưu, không chạy lại preflight để thu thập dữ liệu đã có.

## Fixed scope from supplied evidence

- Tenant: `95f5db58-4233-4dfe-9747-5276105af12a`.
- Project: `188`; core job: `2f3664eb-cdf6-44bd-aa6f-8ef12cc8b432`.
- Batch job: `046bcf43-2ecf-4b97-9952-68dfc3f8e9eb`.
- Logical ID: `render_job_scene_video-job-046bcf432ecf4b97995268dfc3f8e9eb-scene-{scene_id}`.

| Scene | Exact job UUID | Exact version UUID |
|---|---|---|
| 831 | `30663c04-ea85-4738-87ed-a93d2414f694` | `8f3e7daf-d8d4-4d1f-8c23-07dfd4a7cf9d` |
| 832 | `e130accb-4423-445c-893e-46bce98b63e9` | `da22066a-6e44-42e9-9db9-48f31eba2a8e` |
| 833 | `0404a954-793a-4a2d-8cd1-706de24c9ca5` | `6fe5d520-cf51-414d-a664-2dbcff8d900d` |
| 834 | `47d877ea-1ab9-4f28-bec8-9fc5810d45c5` | `ae0866b5-5b02-46b2-8967-efe39f01e46a` |
| 835 | `1f53cedf-0347-4fff-9e42-1347ba8cec60` | `526282e5-00f6-4a80-ab68-2d34a8017745` |
| 836 | `fca7c1b7-5829-495f-90e2-e47f5e074651` | `1ed9070a-c7ee-4c9a-aa4b-7b6836c74b1e` |
| 837 | `236d182e-2c02-4238-8f87-8b9892e7bdf3` | `af52ed9a-a04b-455a-b565-c5b9c130dc6d` |

Known evidence is accepted without asking for duplicate queries: all jobs pending_reconciliation with SCENE_VIDEO_MANUAL_REVIEW_REQUIRED; all versions queued; no task/base on job/version; no submit intents in the supplied preflight; expired leases at inspection; all ownership checks true; no selected video; guard failure text on all workers; job events JOB_QUEUED/WORKER_CLAIMED/JOB_STATUS_CHANGED/JOB_PENDING_RECONCILIATION and no observed submit-confirmation event. DTO root cause was fixed at 0550ebc5. No DTO or Submit Guard is modified here.

The displayed job updated_at values (.397/.483/.571/.671/.764/.834/.916 at 2026-10-22 20:47:29 +07) are **informational only**. They are not used as exact timestamp predicates. The supplied evidence supports a pre-HTTP failure, but operator expressly noted it is not independent provider/outbound-HTTP proof. Nothing in this package manufactures that missing evidence.

## Supplemental data already collected — reuse existing exports

Operator reports the seven-row supplemental_manifest is already collected. Do not rerun `project188-scenes831-837-preflight.sql` or the old inventory merely to copy existing values. The retained supplemental script documents the origin of these four result groups:

1. Database/operator/server identity.
2. The referenced schema/column types (including enum labels), defaults, CHECK/FK/unique constraints, noninternal triggers and trigger-function hashes, table SELECT/UPDATE/INSERT grants and row-security visibility; returns a schema signature plus details for review.
3. Audit-ID sequence usage privilege where a serial/identity sequence exists.
4. Exactly the seven supplied UUID pairs, with current job and version updated_at rendered as UTC **six-microsecond-digit** timestamps; full-row fingerprints; current lock/worker/lease/retry and identity/state drift facts; a JSON supplemental manifest with all approvals false.

The exact timestamps/hashes are reported collected by the operator. The raw seven-row JSON payload is not present in the context accessible to this editing session; Codex has not reconstructed it or filled values from the old millisecond display. Use the original saved manifest verbatim. Full-row hashes bind hidden precision and other concurrent changes without exporting whole prompt/input/config rows. The script does not re-export old event history, versions or intents. The repair transaction revalidates them atomically at execution; that is a safety predicate, not a request to repeat the old operator preflight manually.

An expired lease remains only a database observation. The operator must positively fence/drain old worker/watchdog ownership at execution time. Independent HTTP/log evidence is reviewed outside SQL; SQL cannot prove a process stopped or authenticate an external log. If application logs truly cannot be obtained, record UNAVAILABLE and the reason; this is not itself proof of no submit. No-submission approval still requires adequate individual evidence. If any job has missing/conflicting outcome evidence, do not approve that job: the entire transaction refuses to proceed, without partial updates.

## Production compatibility gate

**SCHEMA_SIGNATURE_RECORDED; SCHEMA_SEMANTICS_REVIEW_PENDING.** Operator-supplied database `todo_saas`, PostgreSQL `17.10`, role `postgres`, schema signature `b9284fc184186344f46a7e58e7c558dd` are recorded exactly. The expected database/signature declarations are populated; `v_schema_reviewed` remains false. A digest alone does not expose the actual constraints, grants or trigger bodies. Review the already-collected schema output; do not requery it solely because Codex cannot see its content. Source DDL and current services support the referenced columns and failed status, but this is not live-schema verification. Trigger bodies are hashed (not exported); an authorized operator must inspect relevant definitions and possible billing/selection/external side effects as necessary. A fingerprint detects drift, not semantic safety.

The same metadata/signature query is embedded in reconciliation. It must match the reviewed supplemental output for the same database and operator role. Missing tables/read privileges, required UPDATE/INSERT grants, changed constraints/triggers/roles or filtered RLS visibility stop the transaction. row_security=off makes filtered reads error; it grants no RLS bypass and does not disable policies. Do not grant extra rights or weaken RLS merely to force execution. Audit sequence usage, if required, must be available; an INSERT privilege failure rolls back all work.

The actual new-operation Submit Guard SQL is copied from main's `SceneVideoSubmitCoordinator.SceneSubmitBlockReasonSql` into reconciliation and postcheck, with only parameter bindings changed. No guard-source edits occur in this delivery.

## DBeaver — operator-only sequence

1. Use the supplied exact mapping above and original saved supplemental_manifest/schema export. Retain that existing evidence; do not run either preflight merely to recollect values already available. Fence/drain the old workers/watchdog under operator control and keep them fenced throughout reconciliation/postcheck. No IIS restart/deploy command is supplied.
2. Check all seven supplemental drift flags and lease observations. Save exact microsecond timestamps and full-row hashes from this snapshot. If original supplied facts changed, stop and review only the changed operation; never choose another job/version or replace the fixed UUID list.
3. Review the existing schema/constraints/grants/triggers export for todo_saas / PostgreSQL 17.10 / postgres and the recorded schema signature. Verify failed status and audit inserts/default IDs are supported; exclude unwanted or nontransactional trigger effects. SQL parse validation is not production compatibility evidence.
4. Copy the original seven-row supplemental JSON manifest verbatim into `v_manifest` in reconcile.sql. Never synthesize missing JSON, microsecond timestamps or hashes; the SQL placeholder remains [] because the raw payload is not accessible here. Keep all approvals/evidence values unchanged until the individual reviews below actually succeed. For each of the fixed seven entries, review evidence and fill approved=true, workerFenced=true, httpOutcome=CONFIRMED_PRE_HTTP_NO_SUBMIT, noSubmitEvidenceReference, projectEventsReviewed=true, projectEventsReference, and applicationLogsStatus=REVIEWED_NO_SUBMIT with a reference or UNAVAILABLE with the explicit reason. Project logs may not repeat the guard failure, but they must be reviewed for conflicting progression. Do not invent observations/references. The live transaction requires an exact stored JOB_PENDING_RECONCILIATION guard-error event for each job and captures its IDs in audit. Missing/mismatched evidence is a specific stop, not permission to fabricate an event.
5. Fill only the operator declarations: v_approved/v_schema_reviewed, database name, schema signature, operator, approval/ticket reference, unique operator-generated batch UUID and reviewed manifest. Keep c_targets unchanged. No job.updated_at value from the old millisecond display may be used: use the exact supplemental strings. Reconciliation checks both precise timestamps AND full-row hashes.
6. Execute the **entire reconciliation SQL script** in DBeaver. Never execute selected UPDATEs. It uses SERIALIZABLE isolation, bounded waits and the backend scene advisory lock order; locks all seven scenes plus relevant version/job/intent rows. Every target is checked before any row update. A failure in any job rolls back all seven. On error issue ROLLBACK, stop and refresh only the now-stale supplemental snapshot before another approved attempt. Never remove a guard or skip the failed entry.
7. Replace the single `NULL::uuid AS batch_id` in postcheck.sql with the exact approved batch UUID. Execute the entire **read-only** script immediately after commit, before a new render. Require summary PASS=true: seven exact UUID pairs, seven job audits and seven identical project audits; correct failed reason/status; no target identity/intent; cleared lease/retry; unchanged protected rows/selection/images/billing/other history/project/core/batch; zero active scene jobs and backend block_reason=0 for every scene.
8. Only then may a user explicitly request a NEW operation with new job/logical identity. No script creates a job/request, requeues, resets attempt counters or renders. Normal UI/backend/input/points guards remain authoritative. A new competing operation seen by postcheck is a blocker, not a reason to override it.

## Exact transaction changes and preservation

Only the seven fixed job UUIDs and seven fixed version UUIDs are UPDATE targets. The operator manifest cannot substitute another mapping. UPDATEs require exact tenant, owner, scene, status and full-row snapshot; no UPDATE uses a project-wide predicate. Initial status must remain pending_reconciliation / queued, job error_code SCENE_VIDEO_MANUAL_REVIEW_REQUIRED; selected video remains absent; no provider identity/result/submitted evidence, no target intent, no competing unresolved operation and no conflicting submit/provider/poll progression event may exist.

Jobs become failed with `RVIDEO_PREHTTP_DTO_OWNER_RECONCILED`; lease/retry ownership is cleared; completion/update timestamps are set. Queued nonselected versions become failed with the same reason while retaining identity, input/config, source images, output/storage pointers, financial columns and all history. No task ID is deleted. No submit intent is created or rewritten. This is a **pre-HTTP no-submit resolution**, not a claim that a provider task terminal-failed.

Append-only audit uses the existing job/project event tables (7 rows each), unique batch ID, operator/ticket, schema/evidence references, captured guard-event IDs, minimal before-images of changed fields and protected-row fingerprints. Raw prompts, credentials and signed URLs are not copied into new receipts.

Before commit the script compares complete protected job/version rows, entire scene/image-version rows, all other scene video versions, matching billing records, project and core/batch parent job rows. It also rechecks absence of target intents, active jobs and the exact current backend blocker. Any detected mutation triggers rollback. This does not substitute for review of triggers that affect unrelated tables or external/nontransactional systems. No refund/rebill, image/selection write, media/storage deletion or history removal is performed.

MD5 row/schema values are change-detection fingerprints, not signatures or independent evidence of no HTTP. UTC is fixed for stable timestamp JSON. A previously used audit batch is rejected; use postcheck, not replay. Reversal requires a separate approved plan and must never automatically requeue an old job or delete audit.

## Local validation / operator work remaining

All three SQL files parse with local pglast v8.7; the DO block parses as PL/pgSQL. No SQL is executed; no database connection or test suite is used. Guard copies and immutable mappings are checked against main/source and supplied IDs. Source references: database/V008_Render_Jobs_Queue_Ready.sql, database/scene-media-versioning/01_add_scene_media_versioning.sql, current RenderJobService/SceneMediaVersioningService/SceneVideoSubmitCoordinator.

Package structure and exact targets are complete. Supplemental data is reported collected and should be reused. **Execution remains blocked** pending verbatim insertion of the saved manifest (not reconstructed here), review of the existing schema/grant/trigger evidence, per-job no-submit approval/references and verified worker/watchdog fencing. See [the evidence/fencing handoff](project188-scenes831-837-HANDOFF.md). Production schema compatibility and successful reconciliation cannot be claimed from local parsing. No provider call, Render trial, production SQL, restart or deploy is performed.
