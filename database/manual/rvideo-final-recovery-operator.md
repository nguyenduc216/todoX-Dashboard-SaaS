# RVideo final recovery ? operator handoff

CODE: BLOCKED for end-to-end acceptance. Project 188: HOLD. Scene 05: HOLD. No production action is authorized by this document.

Baseline reproduced from main a47eda86 (243 tests: 228 passed, same 15 failed; the extra passing test is the new adapter test). All 15 failures are RVideo-related; none is an unrelated subsystem failure. Two test-fixture defects are fixed here. The remaining 13 are source-text assertions inconsistent with current source, not demonstrated production regressions. No production behavior is changed to satisfy them. Full E2E is NOT RUN: no isolated PostgreSQL engine/test database was available or supplied; unit/UI-method/adapter tests do not prove transaction/FK/worker finalization. No production database is used as a substitute.

| Baseline failing test (short name) | Finding |
|---|---|
| ResolveNextAttemptIndexReusesActiveAttemptAndSkipsFailedOne | Fixture supplied two arguments to a three-argument method; fixed with typed candidate array. |
| PendingPollSurvivesMoreThanThreeWorkerClaims | Dynamic Add cannot access private nested candidate type; fixed using IList and one persisted owner across twelve claims. Duplicate-owner rejection has its own regression test. |
| SceneVideoChildJobPersistsResolvedProviderAndModel | Requires route.ModelName spelling; child model is resolved through current policy/override. |
| RVideoWorkerPersistsSafeHttpSubmitDiagnosticsAndDoesNotFallbackKnownNotResourcesTask | Rejects authorization text even in the diagnostic redaction denylist. |
| RVideoPromptAssistantStillRejectsMissingServiceIdBeforeGeneration | Searches removed PromptAssistant.GeneratePromptAsync; current call is RVideoPromptAction.GenerateAsync after service validation. |
| RVideoRetryUiDisablesTheRetryButtonAndFlipsSceneToQueuedImmediately | Requires old attribute; current expression additionally checks CanRerenderSceneVideo. |
| MaxAttemptsDoesNotBlockProviderPollScheduling | Extracts from poll method to SelectJobSql, includes unrelated later methods; not scoped to poll scheduling. |
| RenderVideoJobsPageLoadsCatalogDurationsAndAppliesPerSceneOverride | Requires direct provider catalog call replaced by shared model-options service. |
| RecoverySafeSchedulerExcludesTerminalStatusesAndPreservesIds | Forbids terminal/identity mentions in the whole method, including safety predicates; not a mutation check. |
| SceneVideoWorkerSupportsSameModelDurationFallbackBeforeModelFallback | Requires removed diagnostic literal; duration resolver/event/classification remain present. |
| SceneVideoWorkerValidatesPersistedPayerBeforeBillingAndProviderSubmit | First SubmitAsync substring matches AssertJobMaySubmitAsync before actual adapter submit. |
| RVideoSubmitFailureReleasesCandidateBillingBeforeFallback | Branch marker changed to require submit token and definitive rejection; old substring lookup fails before assertion. |
| CompletionRequestCarriesProviderDurationSeparatelyFromSceneDuration | Numeric assertions pass; exact newline/indentation source match fails. |
| TransientPollFailureWithPersistedTaskUsesProviderPollScheduler | Requires old pre-submit retry/fallback text; confirmed-task path uses polling-first. |
| SceneVideoSubmitIntentAllowsCompletedTaskAndExplicitlyRejectedFallbackOnly | Requires old adjacent SQL substring; current guard includes audited terminal-failure ownership/evidence checks. |

Validation after fixture fixes: focused suite 40/40 PASS; baseline classes 231 PASS / 13 source-assertion FAIL; Release build exit 0 (52 warnings, 0 errors); git diff --check passed. SQL/PLpgSQL syntax parsed for both recovery packages without execution. No E2E PASS is claimed.

## One maintenance/recovery procedure (operator only)

1. **Approve evidence before maintenance.** Reuse saved manifest/schema/events; do not repeat completed preflight. Schema/identity review is recorded as operator-confirmed; no new schema migration or explicit nextval patch is justified. Copy the original seven-row supplemental manifest verbatim into the existing disabled script; exact timestamps/hashes are not reconstructed here. Project 188 still needs per-job coverage from earliest execution through fence across all attempts/hosts: saved stdout/rotations plus existing outbound proxy/APM/network traces or an authoritative complete execution record. Missing submit markers alone are insufficient. Record retention/sampling/clock gaps and safe evidence references. Keep no-submit approvals false until each review passes. Scene 05 is a separate confirmed-task case: operator checks the existing authenticated provider console/history or support record for task c6ddf269339eed5a and base d6556aa42242fc37 in the correct account/project. Preserve dated sanitized raw terminal evidence/reference. If console uses video-base lookup, correlate both IDs; source PollAsync sends its supplied identity as videoId to the configured runtime PollPath, so do not invent a public URL or blindly substitute IDs. No provider request is run here. Missing/running/unknown outcome means HOLD; success requires result recovery, not the terminal-FAILED repair.

2. **Inventory all owners and active work.** Known application todoX.SaaS/, pool todoX.SaaS, path H:\WEBDEPLOYED\todoX\saas. PID 11204 is an old observation, not current identity. TodoXRenderAPI Stopped does not cover TodoXAutomationAPI/TodoXTikTokIngestAPI Running; verify their deployments/configuration and whether they load TodoX hosted workers or enqueue work. Inventory every replica/host/service/scheduled task with the deployment owner; absence on one machine is not fleet-wide proof. Use existing worker/lease evidence first. Only for the still-missing maintenance-wide active-work inventory, operator may run this read-only query in the approved database context (includes all tenants affected by a shared application pool):

```sql
BEGIN READ ONLY;
SELECT id, tenant_id, job_type, status, lock_owner, worker_key, lock_until, retry_after,
       attempt_count, max_attempts,
       (NULLIF(btrim(provider_task_id),'') IS NOT NULL
        OR NULLIF(btrim(provider_video_id_base),'') IS NOT NULL) AS has_provider_identity
FROM render.render_jobs
WHERE status IN ('queued','preparing','rendering','processing','post_processing','pending_reconciliation')
ORDER BY tenant_id, job_type, created_at, id;
COMMIT;
```

Read-only Windows inventory on each known server (no command lines/credentials exported):

```powershell
$appcmd = Join-Path $env:windir 'System32\inetsrv\appcmd.exe'
& $appcmd list app /apppool.name:todoX.SaaS /xml
& $appcmd list apppool 'todoX.SaaS' /text:state
& $appcmd list wp /xml
Get-CimInstance Win32_Service | Select-Object Name, State, ProcessId
Get-CimInstance Win32_Process | Where-Object Name -in @('w3wp.exe','dotnet.exe','TodoX.Web.exe') | Select-Object Name, ProcessId, ParentProcessId, CreationDate
Get-ScheduledTask | Select-Object TaskPath, TaskName, State
```

3. **Authorized drain and verified fence.** Maintenance owner approves all shared-pool/tenant impact, prevents new enqueues and automatic takeover, and safely drains every identified writer/outbound operation. Operator alone stops the verified pool through IIS Manager and applies authorized controls to separate workers/replicas. Do not recycle as a fence, clear leases or revoke shared database credentials. Recheck pool Stopped, old/new PID generations and associated child workers gone, other service/host owners fenced with acknowledgements, no outstanding ambiguous HTTP and no restart/takeover. Expired leases are insufficient. Keep fencing active through postcheck; do not send browser/health requests to verify a stopped application.

4. **Separate approval gates and transactions.** Project 188: only all seven individually approved no-submit histories, exact saved snapshots/schema sign-off and verified fencing permit the full existing project188-scenes831-837-reconcile.sql. Scene 05: only authentic terminal FAILED evidence for both task/base, matching owner/token/snapshots, schema sign-off, maintenance approval and verified fencing permit scene05-job-f991604f-reconcile.sql. Never apply the Project 188 no-task procedure to Scene 05. Approval flags remain false until operator actually signs. Any error: ROLLBACK, review drift; no forced status or identity edits. Never execute selected UPDATE statements alone.

5. **Postcheck before restoring service.** Run each existing read-only postcheck for the corresponding approved repair, using its exact audit/batch context. Project 188 requires seven matching job/project receipts, failed exact jobs/versions, preserved protected hashes (billing/points/images/selection/parents), no target intent and block_reason=0/active_jobs=0 for all seven. Scene 05 requires unchanged V2 completed/selected and all history, retained task/base/billing, matching reconciliation audit, block_reason=0 and zero active competing jobs. Any mismatch keeps HOLD and maintenance owner investigates; do not blindly rerun. Only after successful postchecks and resolved in-flight outcomes does operator restore the previously recorded service states under maintenance approval. Refresh UI; any user-initiated new render is a separate controlled runtime acceptance, not an action of these scripts.

Existing scripts: [Project 188 README](project188-scenes831-837-README.md), [Project 188 postcheck](project188-scenes831-837-postcheck.sql), [Scene 05 README](scene05-job-f991604f-README.md), [Scene 05 postcheck](scene05-job-f991604f-postcheck.sql). No billing, points, image versions, selected completed video, submit guards or provider identities are modified by this delivery. Neither recovery is declared executed or unlocked.
