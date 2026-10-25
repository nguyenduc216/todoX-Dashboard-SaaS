# Supersede pre-submit guard P0

CHANGE_ID: TODOX-20261025-SUPERSEDE-PRESUBMIT-001. FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER.

## Root cause and correction

The shared SceneSubmitBlockReasonSql excluded only jobs directly acknowledged by an ACTIVATED transition through scene_video_job_is_superseded. In a scene with several historical attempts, an earlier failed job with a CONFIRMED_PROVIDER_TASK intent remained outside that set. It therefore returned ExistingIntentUnresolved even though generation 1 was ACTIVATED, the head owned the new job/version, and the new job had no intent or provider request. The same limitation affected historical job/version refusal clauses.

The application guard now derives the current activated generation from matching head, transition, job, version, logical request and generation identities. It excludes only linked earlier scene/project operations with a lower version number and a lower (or legacy absent) operation generation. Version-side generation metadata must also be earlier. An intent must match its historical version's logical request. Ambiguous ownership, missing activation proof, malformed generations, same/future generations and the current operation still block as before. This does not declare any old provider outcome terminal or change is_superseded semantics.

The existing scene/head locks, physical-send lease, BeginSubmit CAS, per-logical unique intent and current-job provider checks are unchanged. No historical jobs, versions, intents, provider identities, billing or selections are rewritten by this fix. Generation zero gets no new historical exclusion.

## Validation

99 focused tests PASS, zero failed/skipped, including 17 cases in SceneVideoOperationDraftPostgresTests (15 local PostgreSQL scenarios and 2 unsafe-connection rejection cases). PostgreSQL 17.10; only fresh UUID-named loopback databases. Ten new cases cover Scenes 865/866/869, all three historical refusal clauses, initial/PREPARED conservatism, missing activation proof, same/future/malformed/unknown operations, logical-request mismatch, concurrent claims and BeginSubmit, token protection, historical-row equality and unchanged points. Defensive job/version cases import an already-activated history; the prepare service's existing refusal of live unacknowledged competitors was not loosened.

The older physical-send lease must finish before preparation can establish its generation boundary; existing activation-lease tests also pass. Generation-zero video, image completion and current final merge regression tests pass. Release build passes with zero errors and existing warnings. No provider/FFmpeg/production E2E was run.

## Operator handoff

No ADDITIONAL migration is required after Supersede V1 for this P0. The guard SQL is compiled into TodoX.Web; no database function, table, trigger or migration file changed. Do not rerun V1 forward on an already migrated database. Install the matching application package using the operator's maintenance procedure, preserving production configuration and uploads. Codex performs no production SQL or IIS deployment.

Keep already-failed generation-1 job e417e3f6-30b6-4b34-a9ba-2486c13e3986 failed, with its existing error and no submit intent. Do not set it queued, use a forced retry, resume its old consent/idempotency key or resubmit its logical request. After the fixed application is deployed, the authorized user uses **Tạo lượt video mới** for the affected scene, refreshes the acknowledgement and explicitly accepts a separately charged operation. A fresh consent/idempotency key creates new job/version/logical identities and advances generation 1 to generation 2. The test covers this through the existing draft/activate APIs, preserving the failed job row and its zero intents. Repeat only for the scenes the user explicitly chooses. Old provider-task results remain unknown history; this is not reconciliation or provider cancellation.

Commit identity is supplied with the package manifest and the final report. Publish is a local package operation only.
