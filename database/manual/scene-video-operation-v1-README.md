# Scene video operation supersede V1

Purpose: an authenticated user explicitly acknowledges one exact historical operation and the extra-cost risk, then starts one new video generation. This does not establish or change the old provider outcome. It does not cancel provider work, fail historical jobs, reconcile billing or auto-render seven scenes.

Operator deployment order (Codex has performed none of these production actions):

1. Review `scene-video-operation-v1-preflight.sql` and the existing schema export. Require `draft`/`queued`/`cancelled` job statuses, the existing submit-intent schema, nullable source-image FK and the final-video table. Review existing triggers/rights. The local integration fixture establishes the tested contracts, not production-schema equivalence.
2. In an approved maintenance window, prevent enqueues and fence **every** worker/watchdog/replica, including old IIS process generations. Do not run mixed application versions: old binaries do not carry writer scopes. Keep config/uploads/secrets unchanged.
3. Operator applies the entire `scene-video-operation-v1-forward.sql` transaction, then deploys the matching application build to all instances before restoring service. Migration creates only generation/transition structures and writer fences; it changes no historical job/version outcomes, selection or billing rows. No new publish is produced by this task.
4. Refresh V2 or Legacy. For Project 188 choose **Tạo lượt video mới** separately on each desired scene 831–837. For Scene 814 use the same explicit action; its completed selected video remains selected until the new result succeeds. Read the cost warning and confirm. Merely opening the page creates nothing.
5. Use `scene-video-operation-v1-postcheck.sql`: require ownership/state consistency, the activated head, separate new job/version/logical identities, and one intent per new logical request. Inspect new execution normally. Activation is not evidence of provider acceptance/completion. Do not retry the old task to obtain a new operation.

Two transactional stages: PREPARED creates an unclaimable draft plus its owned queued version and exact consent receipt; ACTIVATED atomically advances the head and queues that draft. A response retry resumes the same key. Snapshot drift refuses activation; fresh explicit consent may abandon only an unsubmitted draft, retaining its receipt. Abandonment is not historical reconciliation.

Claim/Begin and the physical submit use the generation fence. Activation waits for an existing physical-send lease; otherwise an old lease/Begin is rejected. Late old completion/failure/mux cannot change current scene output. Merge checks the generation/selection snapshot captured before reading inputs. Worker output paths are version/job specific. Explicit user selection remains separate from worker mutation.

Rollback is only for an unused migration with **zero** heads/transitions, with matching application rollback under maintenance. `scene-video-operation-v1-rollback.sql` refuses to erase used history, including ordinary generation-zero allocations. After use, retain schema/fences and prepare a reviewed forward repair instead.

Historical Project 188 operations and task `c6ddf269339eed5a` remain separate audit/reconciliation work; supersede does not call them failed/no-task. Provider calls and production acceptance have not been exercised here.

Local validation: 71 focused cases passed, zero failed/skipped, including two isolated PostgreSQL 17 contract/concurrency scenarios and real component action/selector tests. PostgreSQL/PLpgSQL parsing and Release build passed. These are not a full browser/provider end-to-end run. Legacy reconciliation write scripts require review against the new immutable-history fences after activation; do not remove those fences to reuse an older script.
