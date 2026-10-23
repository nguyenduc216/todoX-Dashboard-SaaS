# Scene 05 — Project 183 / Scene 814

Only job `f991604f-c72e-4b4e-8c4d-71d232e854f9`, task `c6ddf269339eed5a`, video base `d6556aa42242fc37` can be reconciled by these scripts. They have not been run against production. No terminal provider evidence was supplied with the request; V9/V10 failed and job rendering do not prove the provider task ended.

## Required evidence and approval

Use an existing saved sanitized provider response/log or provider support confirmation that identifies **both** task IDs and establishes irreversible terminal failure. Explicitly check it is not a local timeout, parser/network/storage failure, NOT_RESOURCES, missing/404 response, empty output or merely no recent progress. An inactive task without an authoritative final outcome is insufficient. No provider call is made by these scripts.

Record the evidence reference, observed time, approval/ticket reference and approving operator. Do not insert a fabricated FAILED payload to satisfy the input checks. SQL can validate identity and attestations; it cannot independently authenticate external provider evidence.

Fence/drain the old worker for this job under operator control before a fresh preflight. A stale/expired lease does not establish the old process is dead. The script refuses a live lease and a changed `updated_at`; it never steals a lease. No IIS action is performed by this delivery.

## DBeaver sequence

1. Review/install the matching source guard change through the normal operator release process. The previous release intentionally blocks any failed job that still has a confirmed task. The delivery publishes local artifacts only; the operator must verify the server DLL hash before executing reconciliation.
2. Run `scene05-job-f991604f-preflight.sql` as a complete SQL script. It is read only. Verify the database, tenant, V9 UUID/logical request, intent owner token, job `updated_at`, selected V2, V10 failed, lease, all scene jobs/intents and evidence. Save these results with the approval.
3. Only after approval and sufficient evidence, edit the declarations in `scene05-job-f991604f-reconcile.sql`. Defaults are `false`/`NULL`/`{}` and deliberately abort. Fill `v_evidence` with exact `providerTaskId`, `providerVideoIdBase`, `normalizedStatus` (`FAILED`), `terminal` (`true`) and the **real** nonempty sanitized `rawResponse` object. Keep credentials out of the evidence.
4. Execute the entire reconciliation script with DBeaver's Execute SQL Script, not a selected UPDATE. It uses one transaction, bounded lock waits, the same scene advisory key as the application, row locks, identity/snapshot guards, immutable audit before-images and the exact backend new-operation guard before commit. On any exception, execute `ROLLBACK;`, inspect the cause and repeat preflight; never remove a guard to force it through. If DBeaver prompts about pending changes, roll back on any error.
5. Run `scene05-job-f991604f-postcheck.sql`. Expect old job failed, lease/retry cleared, exactly matching audit, unchanged V2/scene/all video versions, no active scene job and new-operation blocker `0`. Refresh UI: selected V2 remains completed; latest render is failed. Render again is enabled only when image, motion prompt, provider, aspect and resolution inputs are valid.

## Exact changes and exclusions

The transaction updates only the named job and its exact confirmed intent, and appends existing job/project audit events. Job status becomes failed. Intent retains `CONFIRMED_PROVIDER_TASK`, its token and both real IDs; `reconciled_at` plus the matching audited `TERMINAL_FAILED` resolution closes it for new operations. It is **not** rewritten as `CONFIRMED_NO_TASK`, and the old job's own durable-submit guards still prohibit resubmission.

The shared submit guard releases this closed confirmed task only when failed job, nonempty task IDs, exact token/IDs, approval/evidence metadata and matching audit all agree. Missing/conflicting evidence continues to block. A different active job or remaining blocker aborts the transaction; these scripts do not repair other jobs/scenes.

No schema change/new enum value, version update, selected pointer/media URL change, scene status update, success marking, wallet/billing mutation or new render is performed. V2 and the full scene/version rows are compared with before-images before commit. Existing V9/V10 remain failed. Billing output in preflight is informational and requires separate review if an unrelated accounting issue exists.

Rollback is not an automatic requeue or deletion of audit. The append-only audit preserves before-images; a reversal requires a new approved plan with fresh provider/lease checks. An idempotent repeat of the same audited resolution performs no updates.

## Release gate and UI

This package does not establish that the task is terminal or that the scene is unlocked. Compare the IIS runtime DLL SHA-256 with the delivered artifact using operator evidence. A local publish hash is not server evidence. After the reviewed code is deployed by the operator, run fresh preflight, supply authentic terminal evidence and fence the old worker, execute the guarded reconciliation, then run postcheck. Only block_reason=0 AND zero active jobs AND unchanged V2/all versions satisfy the release gate.

The UI now shows only user-facing status and keeps Render again visible. It does not override a blocker. Failed V9/V10 and an internal provider_failure are not terminal provider evidence. Provider identities and audits remain in the database/backend.
