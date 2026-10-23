# Project 188 — evidence and fencing handoff

## Recorded facts and present limits

- Database: `todo_saas`; PostgreSQL: `17.10`; role: `postgres` — operator-supplied, not queried by Codex.
- Recorded production schema signature: `b9284fc184186344f46a7e58e7c558dd`.
- Exact tenant/core/batch/job/version/logical mapping remains the fixed seven-pair mapping in README/reconciliation.
- Operator reports a complete saved supplemental_manifest with precise timestamps and row hashes. **Reuse that original payload verbatim. Do not rerun preflight to recollect existing data.** The raw seven-row JSON is not present in this session's accessible conversation/files; no timestamp/hash has been reconstructed. `v_manifest=[]` remains an execution stop until the existing payload is inserted.
- No approvals/evidence/fencing have been established. `v_approved=false`, `v_schema_reviewed=false`, each per-job approval remains false/unverified; there is no current declaration of CONFIRMED_PRE_HTTP_NO_SUBMIT or workerFenced=true.

The recorded digest can be compared with the signature recomputed inside the guarded transaction. It does not independently reveal or authenticate the contents of constraints/grants/triggers. Use the already-collected schema export to review those semantics. The signature query is role-sensitive through privilege/RLS facts: use the recorded role/context or treat a mismatch as a stop. A collected snapshot can become stale after collection; the existing transaction's timestamp/hash checks remain mandatory, but do not collect it again merely for handoff. If a real runtime drift error occurs later, stop and review that new change separately.

## SQL/source compatibility review possible locally

The package remains standalone PostgreSQL SQL/PLpgSQL, with fixed UUID targets, UTC microsecond timestamp parsing, row fingerprints, schema-drift check, SERIALIZABLE transaction, scene advisory/row locks, exact UPDATE predicates and append-only audit. These features are compatible at the language level with PostgreSQL 17; local parsing does not execute the statements or establish live-column/constraint/trigger semantics.

The source guard query in reconciliation/postcheck is unchanged from main's SceneVideoSubmitCoordinator.SceneSubmitBlockReasonSql except bound parameters. The DTO fix at 0550ebc5 and ownership/Submit Guards are not modified. The known schema signature is an expected value, not approval: v_schema_reviewed stays false until an authorized reviewer signs off the saved constraints, column types, audit defaults, privileges and trigger side effects. The same actual metadata must still match inside the transaction. Do not infer semantic review from the role name postgres or disable triggers/RLS to make a check pass.

## Per-job review record — all currently unapproved

Use one evidence bundle for each fixed job, not a project-wide statement. Record job/version/logical ID, worker identity/host/process start time, execution attempts, UTC interval and clock offsets, source/build version at the time of failure, saved references, reviewer and review time. Use the exact UUID table in README.

| Scene | approved | HTTP outcome | workerFenced | Project/HTTP evidence |
|---|---|---|---|---|
| 831 | false | UNREVIEWED | false | UNVERIFIED |
| 832 | false | UNREVIEWED | false | UNVERIFIED |
| 833 | false | UNREVIEWED | false | UNVERIFIED |
| 834 | false | UNREVIEWED | false | UNVERIFIED |
| 835 | false | UNREVIEWED | false | UNVERIFIED |
| 836 | false | UNREVIEWED | false | UNVERIFIED |
| 837 | false | UNREVIEWED | false | UNVERIFIED |

### HTTP/application evidence

1. Start with the already-saved guard failure and JOB_PENDING_RECONCILIATION event for the exact job. Correlate the version/logical request, every WORKER_CLAIMED attempt and any process/worker changes. A guard exception on one attempt does not settle another execution of the same job.
2. Review saved application and outbound HTTP/proxy/APM traces for the complete operation interval: earliest relevant claim/attempt through verified fencing of every old owner, including retries/resumes and clock skew. Record the actual coverage/retention/sampling/gaps. A narrow search for only the error timestamp is insufficient.
3. Search by the job UUID, version UUID, full logical ID and batch/scene correlation. Use the actual configured create-video endpoint found in approved configuration/traces; do not invent an endpoint or call the provider. Avoid exporting credentials, prompts or signed URLs.
4. On builds that actually contain the instrumentation, relevant event names include RVIDEO_VIDEO_HTTP_SUBMIT_REQUEST, RVIDEO_VIDEO_HTTP_SUBMIT_RESPONSE, RVIDEO_VIDEO_HTTP_SUBMIT_RESPONSE_PARSE_FAILED and RVIDEO_79AI_SUBMIT_DIAGNOSTICS. Verify historical build/instrumentation coverage first: absence of a newer event name in an older build proves nothing.
5. A request marker means the request was about to be sent; it is not by itself confirmation of provider acceptance. It nevertheless conflicts with an unqualified no-submit approval and must be investigated. An outbound request/response, recorded task/base, submit intent, ambiguous submission, or unresolved trace gap prevents this no-submit repair. Do not rewrite such an outcome as failed/no-task to unlock a scene.
6. If HTTP logs are unavailable, record applicationLogsStatus=UNAVAILABLE and the reason. That is a limitation, not no-submit evidence. Leave HTTP outcome unreviewed unless an independently reviewed authoritative execution record establishes that ALL executions aborted before the submit path with no alternate operation. The approver must identify the actual evidence, never infer it from no task ID or lease expiry.

### Project event review

Use the existing project-events export and saved sanitized event data, correlated to each exact renderJobId/jobId, sceneVideoVersionId, logicalRequestId and scene/time range. Do not rerun the full old preflight. If the saved export genuinely lacks an evidence item, retrieve only that missing correlated item under normal operator read-only access; no production query is executed by Codex.

Review RVIDEO_VIDEO_SUBMIT_BEGIN, RVIDEO_VIDEO_SUBMIT_UNKNOWN, RVIDEO_VIDEO_SUBMIT_FAILED, SCENE_VIDEO_PROVIDER_SUBMITTED, provider-reuse/polling events and HTTP diagnostics for alternate progression. SUBMIT_FAILED alone does not establish that HTTP was never sent. Missing project error events do not invalidate the known job guard exception, because the source logs that failure in job events; they also do not prove no HTTP. Record projectEventsReviewed only after checking the relevant coverage and reconciling all progression events. The script conservatively rejects SUBMIT/PROVIDER/POLL progression; do not delete/filter these events or weaken the guard to force approval.

## Verifiable worker/watchdog fencing plan — no service action performed

Source review identifies SceneVideoJobWorker (scene claims), SceneVideoReconciliationWorker (unidentified-job escalation and poll scheduling), RVideoLifecycleWorker (lifecycle activity), and RenderJobWorker (batch/other queue work; current generic claim excludes render_scene_video). All are registered in Program.cs. Scene-video lane keys include machine name/lane/GUID; reconciliation identities include machine name/scene-video-review/GUID.

RenderQueue:Enabled is checked at ExecuteAsync startup in these workers. **Editing the flag on disk or observing false in a config file is not proof that an already-running worker stopped.** No supported per-job live pause/fence endpoint was established by this review. No such endpoint, command or safe hot-reload behavior is assumed.

The authorized operator must prepare and verify the following under an existing maintenance/drain procedure. This handoff does not stop/restart IIS/services, issue deployment commands, alter configuration, revoke credentials or change firewall rules.

1. Inventory every host/replica/application pool/service/scheduler/manual process that can claim, retry, enqueue, escalate or write these scene operations, including old deployments and duplicate instances. Correlate saved worker keys with host, PID/process start time and deployment identity. Include external schedulers/manual enqueue entry points; one IIS worker process inventory is not a fleet-wide fence.
2. Record the exact approved maintenance action and its scope/time in a ticket. Using supported operator controls, prevent new affected claims/enqueues/retries AND reconciliation/lifecycle scans from all relevant writers, then drain existing work. If the platform cannot demonstrate that control or scope, do not set workerFenced=true. This document supplies no automatic stop/deploy action.
3. Obtain positive completion/exit/drain acknowledgements for every previously capable owner, with host/process identity and time; account for old process generations and in-flight tasks/HTTP calls. A cancelled token, quiet log, expired DB lease, elapsed interval or pending_reconciliation status alone is insufficient.
4. Verify no old in-flight HTTP call or queued continuation can later submit/write these operations. Inspect the already-collected lease snapshot and the operator's current maintenance/drain telemetry. If a later recheck is needed because a real ownership change occurred, limit it to those changed lease/owner fields; do not repeat old identity/schema collection unnecessarily. Database row locks protect the repair transaction but do not cancel an old network request.
5. Keep the verified fence in force from approval through reconciliation commit and postcheck. Record ongoing control and any automatic restart/recovery policy. A process-exit observation without preventing a new eligible instance from taking over is not a durable fence.
6. Use the existing read-only postcheck while the fence still holds. Any changed snapshot, new intent/task, active operation, failed protection check or nonzero backend blocker means STOP; no partial repair/new render. Restoration of normal worker activity is a separate operator-approved action after accepted postcheck; no requeue/render command is included here.

Optional read-only inventory commands ON EACH KNOWN WINDOWS HOST (authorized operator only):

```powershell
Get-CimInstance Win32_Process |
  Where-Object { $_.Name -match '^(w3wp|dotnet|TodoX.*)\.exe$' } |
  Select-Object ProcessId, Name, CreationDate, ExecutablePath
& "$env:windir\System32\inetsrv\appcmd.exe" list wp
```

These commands do not stop anything and do not prove fencing. Permission failures, unknown hosts or unmapped workers are evidence gaps. Do not paste command lines containing secrets into the handoff. Record approved host/PID/start-time evidence privately.

## Exact approval conditions

Approval is an operator act after evidence review, not a value auto-derived by the script or Codex. For each of the seven entries, all of the following must be true before an approver changes any approval/evidence field:

- The original saved manifest is bound verbatim to the fixed UUID/logical mapping; full-precision timestamps/hashes are not replaced by rounded displays or fabricated values.
- The no-submit evidence covers every execution and has no conflicting request/task/intent, unresolved HTTP outcome or unaccounted log gap. A real noSubmitEvidenceReference names the reviewed bundle, not merely the DTO commit.
- Project event coverage has been reviewed and its reference recorded. Application/HTTP evidence or an explicit limitation is recorded accurately; missing evidence is not promoted to certainty.
- All relevant worker/watchdog writers are positively fenced with verifiable scope/time/identity and the fence remains active through postcheck.
- Existing production schema/constraint/grant/trigger semantics are reviewed for todo_saas / postgres with the recorded signature; no unwanted data/financial/external side effects are assumed away.
- Reviewer identity, approval/ticket reference and unique reconciliation batch UUID are recorded. No seven-job blanket approval substitutes for the individual checks.

Only after those acts may the operator populate the corresponding approval/evidence fields and global flags. Currently ALL remain unverified/false. If any scene cannot meet a condition, do not approve it; the all-or-nothing transaction must not run for the remaining six. Do not alter its guard or target set to create a partial repair.

After an authorized execution, postcheck must prove the exact seven failed job/version pairs with reason RVIDEO_PREHTTP_DTO_OWNER_RECONCILED, matching immutable job/project audits, unchanged protected data/points/images/selection/history/core/batch, no remaining old-operation blocker and no active competing job. Until then the seven scenes are not declared unlocked.

## Missing evidence now

1. Verbatim manifest payload cannot be inserted by Codex from the accessible context; use the original existing operator export, without rerunning collection. No timestamp/hash placeholder has been filled with a guess.
2. Per-job independently reviewed no-submit evidence and project-event coverage references/approvals remain unverified.
3. Positive fleet-wide worker/watchdog fencing and in-flight-drain acknowledgements remain unverified.
4. Existing schema export semantic review/sign-off remains unverified; the supplied signature/database/version/role are recorded, not independently queried.

No production SQL, provider call, trial render, worker stop/restart or IIS deployment is performed. No DTO or Submit Guard source change is part of this handoff update.
