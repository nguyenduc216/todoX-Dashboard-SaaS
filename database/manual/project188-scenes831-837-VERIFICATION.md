# Project 188 — verification before reconciliation (2026-10-23)

## Current decision: HOLD — no reconciliation execution instruction yet

Operator confirms production is deployed with TodoX.Web.dll SHA-256 `89B273F04615917348EC399B1F8DE6EE2B6CFE301755FF91F9A55143E77F48A7` and the updated RVideo UI works. This resolves the previously missing deployment-hash evidence **by operator confirmation**, not a new independent server read by Codex. The locked seven historical operations are not proof that the new DTO fix failed: the existing pending/manual-review states intentionally continue to hold Retry/Render again until their outcomes are safely settled.

No UI, DTO, Submit Guard or application source is changed. No build/publish, production SQL, provider call, trial render, stop/restart or deployment is performed.

## Local evidence actually checked

- 49 readable `.log`/`.jsonl`/`.ndjson` files under local TodoXPortal, excluding generated binary/web assets and rollback copies: zero matches for the seven exact job UUIDs (including compact forms) or exact ownership-guard message; no read errors.
- Local Windows Application Event Log from 2026-10-22: 180 entries, zero matching target-job/guard entries. This is the local development machine, not the production server.
- Local publish web.config reports hostingModel=inprocess, stdoutLogEnabled=false and stdoutLogFile=.\logs\stdout. These local routing values are NOT asserted to equal the preserved production config. Do not enable logging/change config/restart merely for this review.
- Inventory/hash/line-index report: `D:\todoX\Dashboard-web\TodoXPortal\artifacts\project188-verification-20261023\local-log-scan.json`. It contains no raw log lines/prompts/tokens.

These negative local searches do not establish no HTTP in production. The operator-supplied seven guard-failure observations and known job-event list remain accepted supporting evidence, but raw production job/project/HTTP evidence has not been independently correlated here. No event contents or per-job approval has been invented.

## Read-only commands to collect ONLY missing server evidence

Run on the real server using an authorized elevated read-only session. First identify the exact application, physical path and pool; do not infer them from the local folder name:

```powershell
& "$env:windir\System32\inetsrv\appcmd.exe" list app /xml
& "$env:windir\System32\inetsrv\appcmd.exe" list vdir /xml
& "$env:windir\System32\inetsrv\appcmd.exe" list wp /xml
```

Then use the supplied **read-only** collector on each already-identified host. Replace the placeholders with the exact application name/path and EXISTING application/outbound-log directories; do not point it at unrelated data. The UTC start below includes the supplied 2026-10-22 failure interval; broaden it if original claim/retry evidence begins earlier.

```powershell
.\database\manual\project188-scenes831-837-server-readonly.ps1 `
  -IisApplicationName '<exact IIS application name, including / application path>' `
  -PhysicalPath '<actual production physical path>' `
  -SinceUtc ([datetime]'2026-10-22T00:00:00Z') `
  -LogRoot @('<existing application log directory>', '<existing outbound trace directory>')
```

The collector reads app-to-pool/vdir mapping, shared-pool applications, pool state/PIDs, related process IDs/start times/parent IDs, DLL hash, ONLY stdout-routing config attributes and target-job event/log indexes. It does not issue a request to the application/provider, modify config or stop/start anything. Raw log messages/command-line arguments are not exported. Inspect adjacent original lines privately where a marker and job ID appear on different lines; an uncorrelated marker is not per-job proof. File write-time filtering is not an event-time/retention guarantee. Compressed archives/APM/proxy retention and sampling must be accounted for separately. Do not use zero indexed matches as automatic no-submit approval.

Use the already-saved render job/project event export first. If the raw correlated events genuinely are missing, [project188-scenes831-837-evidence-readonly.sql](project188-scenes831-837-evidence-readonly.sql) contains ONLY: exact seven-job event indexes, correlated project-event fields (no prompt/provider-payload dump), and optional current lease observations needed for fencing. It does not repeat schema/manifest/full preflight. Codex has not run this SQL. A scene/time-only project match is explicitly weaker than an exact job/version/logical match and must not be attributed automatically.

The read-only file can be used to retrieve the missing event facts under normal operator access; it is not reconciliation. If those facts already exist in the saved export, review that export and do not query them again.

## Exact per-job HTTP/event approval checklist

Use the immutable seven UUID pairs and logical IDs in README. For EACH scene 831–837 record:

1. The exact stored identity-guard failure and worker/attempt correlation, event IDs and timestamps. Review all attempts/owners, not just the final failure. The source path places this guard before ClaimSubmitAsync/BeginSubmitAsync/adapter SubmitAsync; the guard exception supports no-submit only for the execution that actually raised it.
2. Project events correlated by renderJobId/jobId, version or full logical request, with scene/time-only evidence kept separate. Review any SUBMIT_BEGIN, PROVIDER_SUBMITTED, SUBMIT_UNKNOWN, provider-reuse/poll and HTTP diagnostics. A failed/unknown submit is not proof of no HTTP. The SQL deliberately refuses conflicting progression rather than deleting it.
3. Existing outbound/application/proxy/APM coverage from earliest relevant execution through the verified fence. Record the historical build/instrumentation, hosts, clock offsets, sampling/retention/gaps and safe evidence references. Current deployed hash does not prove which instrumentation existed at the old failure time.
4. Correlate HTTP_SUBMIT_REQUEST/RESPONSE/PARSE_FAILED and submit-diagnostic markers where that build recorded them. A request marker denotes imminent send, not acceptance; it still requires investigation. Actual request/response/task/intent, unknown outcome, another attempt or incomplete coverage means HOLD for this no-submit repair.
5. If logs are unavailable, mark the limitation honestly. Only an authoritative independently reviewed execution record covering every execution can replace them; missing logs, no task ID, lease expiry and the DTO root cause alone must not be promoted to confirmed no-submit.
6. Attach the individually reviewed noSubmitEvidenceReference, project-events review/reference and application-log status/reference. No project-wide blanket approval substitutes for these seven records.

Current state for ALL seven: no-submit approval UNVERIFIED; project/HTTP correlation not independently completed by Codex; worker/watchdog fence UNVERIFIED. All approval flags remain false/unreviewed. No outcome is declared CONFIRMED_PRE_HTTP_NO_SUBMIT.

## Concrete IIS fencing approach — prepare/verify only

Reviewed source registers SceneVideoJobWorker, SceneVideoReconciliationWorker, RVideoLifecycleWorker and RenderJobWorker in the application. The scene worker claims render_scene_video; the generic worker currently excludes that type but may run batch/enqueue work. Reconciliation worker escalates manual review and schedules polls; lifecycle can enqueue/finalize. All relevant instances/writers must be covered, including other hosts/services/schedulers and old process generations.

RenderQueue:Enabled is read at ExecuteAsync startup, not rechecked as a live fence. Editing the config or seeing false on disk does not stop existing loops. No verified per-job online pause/drain endpoint was found. Do not invent one.

The practical IIS isolation option, IF the pool/instance scope is fully established, is an operator-controlled maintenance drain and stop of the owning application pool/processes, held through postcheck. This can affect the ENTIRE application and all tenants/jobs. If the pool is shared, or other work cannot safely drain, the seven-scene approval is insufficient: an authorized maintenance owner must approve the broader impact or provide a verified existing scoped control. No stop command is executed or supplied for blind use here.

Operator sequence under that separate maintenance authorization:

1. Save existing evidence and inventory exact app/pool/physical path, all apps sharing the pool, every host/replica, old pool PID/process start times and any out-of-process TodoX/dotnet child/service/scheduler. For inprocess the hosted services run in w3wp; for outofprocess include the child app process. Verify the PRODUCTION hosting model; the local web.config is not its proof.
2. Prevent new affected user/API/batch enqueues through established maintenance controls and drain running requests/jobs on all identified instances. Record who controls automatic starts/recovery and alternate writers. Do not clear DB leases, revoke the shared postgres login or modify rows to simulate a fence.
3. Only after safe drain/impact approval, the operator may use IIS Manager → Application Pools → the VERIFIED owning pool → Stop, plus the authorized controls for any separate worker service/replica. Do not use Recycle as the fence: old and new generations may overlap. No such action is performed by Codex. If a forced interruption leaves an HTTP outcome unknown, keep no-submit unapproved.
4. Verify with the read-only collector/AppCmd that the pool is Stopped, no old pool PID remains, child/separate worker generations are gone or positively acknowledged drained, no new eligible instance appears, and scheduler/recovery controls cannot take over. A quiet log, one PID disappearance, cancelled token or expired lease is not a fleet-wide fence.
5. Confirm in-flight outbound work is accounted for and there are no queued old continuations capable of later submit/write. Observe current lease/owner facts ONLY if needed for this live fence; they do not themselves prove process drain. Preserve the original supplemental manifest; actual data drift will cause the existing transaction guard to abort.
6. Keep the control active while reviewing/performing the later authorized repair/postcheck. Do not browse/health-check a stopped application as a fencing check: a request may queue or trigger startup behavior. Use process/pool state, existing telemetry and logs instead. Do not auto-start/requeue/render in this verification task.

If any host/owner/drain evidence is inaccessible or unaccounted for, fencing remains UNVERIFIED. Row/advisory locks in the later SQL do not cancel old HTTP requests and cannot replace this step.

## Schema/constraint/trigger review outcome

Recorded production fingerprint: b9284fc184186344f46a7e58e7c558dd, database todo_saas, PostgreSQL 17.10, role postgres. The same fingerprint algorithm is present in the prepared transaction; source guard copies remain aligned. Local SQL/PLpgSQL syntax parsing is possible without a production connection.

A digest alone cannot reveal CHECK/enum values, grants, audit defaults, RLS visibility or trigger side effects. Use the ALREADY-COLLECTED detailed schema export to verify failed statuses, exact column types, event insertion/default IDs/sequence rights, required read/write rights, and transactional/nontransactional effects on points, images, selection, parents or external systems. No new schema preflight is requested merely to restate the digest. v_schema_reviewed remains false until that review is actually signed. The later runtime signature/row checks remain mandatory and fail closed on real drift.

## Gate before any repair instruction

Remain at HOLD until all seven individual event/HTTP outcome reviews and references, fleet-wide fence acknowledgements and existing-schema semantic sign-off are complete; the original saved supplemental manifest must be inserted verbatim without changing unapproved flags or fabricating timestamps/hashes. Only then may an authorized operator decide whether to approve the existing guarded repair and its read-only postcheck. This document does not direct running reconciliation now. Any one unresolved operation blocks the entire batch.

Production deployment-hash confirmation does not settle the old operations. Seven scenes have NOT been declared unlocked.

## Final readiness delta (operator evidence update)

Decision: HOLD. Accept the newly reported job/project-event findings as operator evidence; do not recollect those exports. IIS application is `todoX.SaaS/`, pool `todoX.SaaS`, path `H:\WEBDEPLOYED\todoX\saas`. Claims in stdout without a submit marker are supporting evidence, not complete outbound coverage.

| Scene | Exact job ID | Guard event evidence | Exact guard-event ID |
|---|---|---|---|
| 831 | 30663c04-ea85-4738-87ed-a93d2414f694 | Operator reports exact=true | Not visible in accessible export/context |
| 832 | e130accb-4423-445c-893e-46bce98b63e9 | Operator reports exact=true | Not visible in accessible export/context |
| 833 | 0404a954-793a-4a2d-8cd1-706de24c9ca5 | Operator reports exact=true | Not visible in accessible export/context |
| 834 | 47d877ea-1ab9-4f28-bec8-9fc5810d45c5 | Operator reports exact=true | Not visible in accessible export/context |
| 835 | 1f53cedf-0347-4fff-9e42-1347ba8cec60 | Operator reports exact=true | Not visible in accessible export/context |
| 836 | fca7c1b7-5829-495f-90e2-e47f5e074651 | Operator reports exact=true | Not visible in accessible export/context |
| 837 | 236d182e-2c02-4238-8f87-8b9892e7bdf3 | Operator reports exact=true | Not visible in accessible export/context |

For every row, operator reports FALLBACK_CANDIDATES_RESOLVED then MANUAL_REVIEW_REQUIRED, no exported progression/provider identity, no intent/task, expired lease. Bind the exact event IDs from the ALREADY-SAVED export to each approval; do not invent IDs or repeat collection. SQL audit captures matching IDs at execution. Original supplemental timestamps/hashes must likewise be copied verbatim from the saved manifest, not reconstructed here.

Historical source checked at `0550ebc5^`: HandleCoreAsync dispatches to HandleProviderVideoAsync; the ownership throw is before ClaimSubmitAsync, BeginSubmitAsync and adapter.SubmitAsync for that iteration. The other direct _tasks.SubmitAsync text is inside #if false (LegacyProviderPathDisabledAsync), not an executable alternate route. The ownership exception exits the candidate loop and is excluded from HandleAsync generic retry handling; SceneVideoJobWorker records pending reconciliation. This proves the throwing execution did not reach its later submit, not that every earlier claim/candidate/host did the same. Verify saved attempt counts, earliest claim through fence, full logical-request variants and other job-owned versions/intents against the existing exports. Current SQL rejects additional owned versions, any target intent and conflicting progression. No evidence of a new executable bypass was found in reviewed source; the exact historical deployed binary and all executions still require operator sign-off. The conclusion concerns video-submit HTTP; it is not a blanket claim about every service/network dependency.

SQL review found an evidence predicate mismatch: the read-only exporter accepts the exact guard text in message OR data_json.errorMessage, whereas reconciliation previously accepted message only. Reconciliation and its audit ID selection now use the same exact-text predicate, tenant/job/event-type and job-created-time scope. No approval/fencing/status guard was removed. Newly reported project event types do not by themselves match the existing SUBMIT/PROVIDER/POLL progression rejection; no relaxation is justified.

Schema details/constraints/triggers/grants are not present in accessible local evidence; the signature alone remains insufficient for semantic approval. Keep v_schema_reviewed=false pending review of the existing detailed export. Individual no-submit approvals and workerFenced remain unapproved.

Minimal operator sequence:

1. From saved exports, bind seven exact guard-event IDs and approve each full execution/no-submit history only after resolving gaps, other attempts or ambiguous HTTP outcomes. Review the existing schema details and original manifest; no full preflight rerun.
2. Authorize maintenance scope, inventory apps sharing todoX.SaaS and all replicas/separate workers/watchdogs, block new enqueues and safely drain. Operator alone may stop the verified pool via IIS Manager and control other writers. Expired leases or a recycle do not fence workers. Account for outstanding HTTP before approval.
3. Read-only server checks (run elevated if necessary):

```powershell
$appcmd = Join-Path $env:windir 'System32\inetsrv\appcmd.exe'
& $appcmd list app /apppool.name:todoX.SaaS /xml
& $appcmd list apppool 'todoX.SaaS' /text:state
& $appcmd list wp /apppool.name:todoX.SaaS /xml
```

Require Stopped, no pool worker generations or associated out-of-process/separate worker processes on every host, and verified prevention of recovery/restart/takeover. Maintain this fence through postcheck. These commands alone do not prove other hosts/services are fenced.

4. Only after all approvals, exact saved snapshots and schema review are complete, operator may execute the entire guarded reconciliation in one transaction, then the read-only postcheck with its audit batch ID. Any error means ROLLBACK, investigate actual drift, no forced update. Require seven matching job/project audit receipts, failed target jobs/versions, preserved billing/images/selection/parents, no target intents and zero old blockers. Any new competing operation keeps HOLD. Restore service only under the maintenance owner's procedure after verification; do not auto-render.

No production action, new build/publish or application-source change accompanies this review. Seven scenes are not declared unlocked.
