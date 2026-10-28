# Reconciliation continuation (TODOX-20261015-RVIDEO-RECONCILIATION-IMPL-001)

Track A identity continuation: production reconciliation checks project, scene and logical request against the version row; missing/mismatched evidence is ambiguous manual, not handoff. Focused 14 PASS/8 approval-gated PostgreSQL SKIP; Release build passes. PostgreSQL identity/tenant/exception/event failure remain unverified; no DB execution, migration, publish, staging or push. Details in per-change history.

Further test-only continuation: synchronized lock contention, restart lease/fencing, wrong type/version and duplicate event-less job tests were added using production repository methods. Database tests remain unexecuted pending isolated-DB approval. Project/scene/logical identity and event writer failure remain unverified; no runtime or frozen writer changes were made. Checkpoint PARTIAL.

Four approval-gated PostgreSQL test methods now compile against production repository ClaimAsync/ReconcileAsync, covering lease fencing, concurrency, deadline manual metadata and provider-owned exclusion. Not executed: isolated DB ownership/approval is outstanding; identity and job-event uncertainty scenarios still require implementation. Full-suite TRX has 21 failures in frozen/unrelated owners; baseline UNVERIFIED, not established pre-existing. No migrations or production SQL executed. Publish/commit/push withheld pending verification and checkpoint approval.

# RVID-UI-V2-SCENE-RERENDER-HISTORY

## Explicit new video version restore (2026-10-28)

CHANGE_ID: TODOX-20261028-RVIDEO-RERENDER-RESTORE-001. Legacy/V2 presentation and the existing video submission boundary now distinguish explicit rerender from initial generation. Completed versions remain in history, active/unknown submissions remain blocked, and saved model/mode/duration plus project render settings reach the existing worker. No DB/schema, provider, image, voice, billing or final-merge changes. Focused 50 PASS; full-suite failures and Phase1B compile blocker reproduced on baseline. Release publish verified; see `../TODOX-20261028-RVIDEO-RERENDER-RESTORE-001.md` for exact commands and limitations.

Track B `RVID-VIDEO-SUBMIT-RECOVERY.1` expanded-boundary checkpoint: persisted scene-video error code is projected for future evidence-based UI classification; UNKNOWN rerender remains fail-closed. Selection of completed versions changes only `is_selected` and is not acknowledgement evidence. See `../20261014_RVID-VIDEO-SUBMIT-RECOVERY.1.md`; PostgreSQL atomicity and poll-only Retry remain unverified.

Writer-sync continuation: queued video version creation now shares guarded enqueue's unsuffixed scene advisory lock, acquired before the authorized scene-row lock and version insertion. Other video status/identifier/bind writers and render-job progress writers are not yet covered; UNKNOWN remains fail-closed and PostgreSQL concurrency remains unverified. See existing Track B per-change history.

Track B lifecycle checkpoint: recovered-completion job writer now serializes behind the scene advisory/authorized scene lock while preserving its SQL ownership predicates. Claim still holds a job row lock first; generic status/poll paths remain outside the shared boundary. Callback fencing, new behavioral tests and PostgreSQL race verification remain outstanding. UNKNOWN stays fail-closed; details in existing Track B per-change history.

Track B status/poll checkpoint: the three job status/poll writers have scene-video-only advisory/scene/ownership guards; latest-version binding is required and terminal regressions are blocked. Worker pre-dispatch rendering and callback fencing remain unresolved; behavioral DB tests NOT RUN. UNKNOWN remains fail-closed. See existing per-change history.

Feature: V2 scene re-render and version history. Existing implementation history: `../20261007-2340_RVID-UI-V2-SCENE-RERENDER-HISTORY.1_scene-rerender-history.md`.

## Image enqueue UNKNOWN reliability

CHANGE_ID: TODOX-20261015-RVIDEO-RECONCILIATION-IMPL-001. See `../TODOX-20261015-RVIDEO-RECONCILIATION-IMPL-001.md` for validation and outstanding gates.

UNKNOWN is not evidence of a failed enqueue. Reconciliation is disabled by default; it never enqueues a second job or calls the provider. Positive job evidence routes to manual review until DB-enforced writer serialization is approved. PostgreSQL tests and schema rollout require separate approval.

Validation continuation: disabled startup and invalid scheduling policy are covered by local tests; an approval-gated, two-connection PostgreSQL harness is prepared but SQL concurrency is not verified. See the per-change history for test results and regression triage.
