-- READ ONLY. DBeaver: execute the entire script on the intended database.
-- FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER; CHANGE_ID: TODOX-20261022-SCENE05-RELEASE-002.
-- No provider request is made. DB failed/rendering/NOT_RESOURCES/local timeout is NOT terminal evidence.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;

SELECT current_database() AS database_name, current_user AS database_operator,
       clock_timestamp() AS inspected_at, version() AS database_version;

SELECT j.id, j.tenant_id, j.job_type, j.status, j.provider_code, j.model_code, j.input_json->>'projectId' AS project_id,
       j.input_json->>'sceneId' AS scene_id, j.input_json->>'logicalRequestId' AS job_logical_request_id,
       j.provider_task_id, j.provider_video_id_base, j.error_code, j.error_message,
       j.worker_key, j.lock_owner, j.lock_until, j.lock_until > clock_timestamp() AS live_lease,
       j.retry_after, j.attempt_count, j.max_attempts, j.created_at, j.updated_at,
       j.input_json->'sceneVideoTerminalFailureReconciliation' AS previous_reconciliation
FROM render.render_jobs j WHERE j.id='f991604f-c72e-4b4e-8c4d-71d232e854f9'::uuid;

SELECT s.id, s.tenant_id, s.project_id, s.scene_index, s.status, s.selected_video_version_id,
       s.scene_video_url, s.scene_video_path, s.updated_at
FROM video_render.video_project_scenes s JOIN render.render_jobs j ON j.tenant_id=s.tenant_id
WHERE j.id='f991604f-c72e-4b4e-8c4d-71d232e854f9'::uuid AND s.id=814 AND s.project_id=183;

SELECT v.id, v.tenant_id, v.project_id, v.scene_id, v.version_number, v.status, v.is_selected,
       v.render_job_id, v.logical_request_id, v.provider_task_id, v.provider_video_id_base,
       v.result_media_id, v.public_url, v.storage_key, v.error_code, v.error_message, v.updated_at
FROM video_render.scene_video_versions v JOIN render.render_jobs j ON j.tenant_id=v.tenant_id
WHERE j.id='f991604f-c72e-4b4e-8c4d-71d232e854f9'::uuid AND v.scene_id=814 AND v.project_id=183
ORDER BY v.version_number;

SELECT i.*, j.status AS intent_job_status, j.lock_owner, j.lock_until,
       j.input_json->'sceneVideoTerminalFailureReconciliation' AS terminal_resolution
FROM render.scene_video_submit_intents i JOIN render.render_jobs j ON j.id=i.render_job_id AND j.tenant_id=i.tenant_id
WHERE i.scene_id=814 AND i.tenant_id=(SELECT tenant_id FROM render.render_jobs
    WHERE id='f991604f-c72e-4b4e-8c4d-71d232e854f9'::uuid)
ORDER BY i.created_at;

-- Inventory ALL scene jobs: a different live operation must never be released by this script.
SELECT j.id, j.status, j.created_at, j.updated_at, j.provider_task_id, j.provider_video_id_base,
       j.worker_key, j.lock_owner, j.lock_until, j.error_code, j.error_message
FROM render.render_jobs j
WHERE j.tenant_id=(SELECT tenant_id FROM render.render_jobs WHERE id='f991604f-c72e-4b4e-8c4d-71d232e854f9'::uuid)
  AND j.job_type='render_scene_video' AND COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id')='814'
ORDER BY j.created_at DESC;

-- Existing sanitized provider responses may supply evidence. An app's failure label alone does not.
SELECT event_type, level, message, data_json, created_at
FROM render.render_job_events WHERE job_id='f991604f-c72e-4b4e-8c4d-71d232e854f9'::uuid
ORDER BY created_at DESC LIMIT 100;
SELECT e.event_type, e.message, e.data_json, e.created_at
FROM video_render.video_project_events e
WHERE e.project_id=183 AND e.tenant_id=(SELECT tenant_id FROM render.render_jobs
    WHERE id='f991604f-c72e-4b4e-8c4d-71d232e854f9'::uuid)
  AND (e.data_json->>'jobId'='f991604f-c72e-4b4e-8c4d-71d232e854f9'
       OR e.data_json->>'renderJobId'='f991604f-c72e-4b4e-8c4d-71d232e854f9')
ORDER BY e.created_at DESC LIMIT 100;

-- Informational only: this reconciliation never changes points/reservations/refunds.
SELECT b.logical_request_id, b.status, b.provider_task_id, b.render_job_id
FROM billing.ai_image_billing_records b
WHERE b.tenant_id=(SELECT tenant_id FROM render.render_jobs WHERE id='f991604f-c72e-4b4e-8c4d-71d232e854f9'::uuid)
  AND replace(b.render_job_id::text,'-','')='f991604fc72e4b4e8c4d71d232e854f9';

SELECT conname, pg_get_constraintdef(oid) AS definition
FROM pg_constraint WHERE conrelid='render.scene_video_submit_intents'::regclass AND contype='c';
-- New-operation guard, shared verbatim with backend source (0 = no submit blocker).
WITH p AS (SELECT tenant_id FROM render.render_jobs WHERE id='f991604f-c72e-4b4e-8c4d-71d232e854f9'::uuid)
SELECT g.* FROM p CROSS JOIN LATERAL (
SELECT CASE
    WHEN EXISTS (SELECT 1 FROM render.scene_video_submit_intents i
        JOIN render.render_jobs prior ON prior.id=i.render_job_id AND prior.tenant_id=i.tenant_id
        WHERE i.tenant_id=p.tenant_id AND i.scene_id=814
          AND (
                -- This job's own intent is always authoritative: it protects the double-click,
                -- the automatic retry and any in-flight/ambiguous submit from being resent.
                i.render_job_id='00000000-0000-0000-0000-000000000000'::uuid
                -- Intent that reached the provider but has no recorded task yet: the provider task
                -- may be live, so a new operation must not silently duplicate it.
                OR i.submit_state IN ('SUBMIT_IN_FLIGHT', 'AMBIGUOUS_SUBMIT', 'MANUAL_REVIEW_REQUIRED')
                -- Intent that never reached the provider: it blocks only while its own job is live.
                OR (i.submit_state='SUBMIT_INTENT_PERSISTED' AND prior.status NOT IN ('completed', 'failed'))
                -- A recorded provider task settles the attempt only once its job completed.
                -- Operator reconciliation closes an actually failed task without claiming no task existed.
                -- FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER; CHANGE_ID: TODOX-20261022-SCENE05-RELEASE-002.
                OR (i.submit_state='CONFIRMED_PROVIDER_TASK' AND prior.status <> 'completed'
                    AND NOT (prior.status='failed' AND i.reconciled_at IS NOT NULL
                        AND NULLIF(btrim(i.provider_task_id), '') IS NOT NULL
                        AND NULLIF(btrim(i.provider_video_id_base), '') IS NOT NULL
                        AND COALESCE(prior.input_json->'sceneVideoTerminalFailureReconciliation'->>'resolution', '')='TERMINAL_FAILED'
                        AND COALESCE(prior.input_json->'sceneVideoTerminalFailureReconciliation'->>'jobId', '')=prior.id::text
                        AND COALESCE(prior.input_json->'sceneVideoTerminalFailureReconciliation'->>'providerTaskId', '')=i.provider_task_id
                        AND COALESCE(prior.input_json->'sceneVideoTerminalFailureReconciliation'->>'providerVideoIdBase', '')=i.provider_video_id_base
                        AND COALESCE(prior.input_json->'sceneVideoTerminalFailureReconciliation'->>'intentOwnerToken', '')=i.owner_token::text
                        AND COALESCE(prior.input_json->'sceneVideoTerminalFailureReconciliation'->>'operatorApproved', '')='true'
                        AND COALESCE(prior.input_json->'sceneVideoTerminalFailureReconciliation'->>'oldWorkerFenced', '')='true'
                        AND COALESCE(prior.input_json->'sceneVideoTerminalFailureReconciliation'->>'sceneId', '')=i.scene_id::text
                        AND COALESCE(prior.input_json->'sceneVideoTerminalFailureReconciliation'->>'logicalRequestId', '')=i.logical_request_id
                        AND prior.provider_task_id IS NOT DISTINCT FROM i.provider_task_id
                        AND prior.provider_video_id_base IS NOT DISTINCT FROM i.provider_video_id_base
                        AND NULLIF(btrim(prior.input_json->'sceneVideoTerminalFailureReconciliation'->>'approvalReference'), '') IS NOT NULL
                        AND NULLIF(btrim(prior.input_json->'sceneVideoTerminalFailureReconciliation'->>'observedAt'), '') IS NOT NULL
                        AND COALESCE(prior.input_json->'sceneVideoTerminalFailureReconciliation'->>'evidenceKind', '')='provider_terminal_failure'
                        AND COALESCE(prior.input_json->'sceneVideoTerminalFailureReconciliation'->'evidence'->>'providerTaskId', '')=i.provider_task_id
                        AND COALESCE(prior.input_json->'sceneVideoTerminalFailureReconciliation'->'evidence'->>'providerVideoIdBase', '')=i.provider_video_id_base
                        AND COALESCE(prior.input_json->'sceneVideoTerminalFailureReconciliation'->'evidence'->>'normalizedStatus', '')='FAILED'
                        AND COALESCE(prior.input_json->'sceneVideoTerminalFailureReconciliation'->'evidence'->>'terminal', '')='true'
                        AND COALESCE(jsonb_typeof(prior.input_json->'sceneVideoTerminalFailureReconciliation'->'evidence'->'rawResponse'), '')='object'
                        AND COALESCE(prior.input_json->'sceneVideoTerminalFailureReconciliation'->'evidence'->'rawResponse','{}'::jsonb)<>'{}'::jsonb
                        AND NULLIF(btrim(prior.input_json->'sceneVideoTerminalFailureReconciliation'->>'operator'), '') IS NOT NULL
                        AND NULLIF(btrim(prior.input_json->'sceneVideoTerminalFailureReconciliation'->>'evidenceReference'), '') IS NOT NULL
                        AND EXISTS (SELECT 1 FROM video_render.scene_video_versions owner
                            WHERE owner.tenant_id=prior.tenant_id AND owner.render_job_id=prior.id
                              AND owner.id::text=prior.input_json->'sceneVideoTerminalFailureReconciliation'->>'ownerVersionId'
                              AND owner.scene_id=i.scene_id AND owner.logical_request_id=i.logical_request_id
                              AND owner.project_id::text=prior.input_json->'sceneVideoTerminalFailureReconciliation'->>'projectId'
                              AND owner.project_id::text=COALESCE(prior.input_json->>'projectId',prior.input_json->>'project_id')
                              AND owner.provider_task_id=i.provider_task_id AND owner.provider_video_id_base=i.provider_video_id_base
                              AND owner.status='failed' AND NOT owner.is_selected)
                        AND EXISTS (SELECT 1 FROM render.render_job_events audit
                            WHERE audit.job_id=prior.id AND audit.tenant_id=prior.tenant_id
                              AND audit.event_type='SCENE05_OPERATOR_TERMINAL_FAILURE_RECONCILED'
                              AND audit.data_json->'reconciliation'=prior.input_json->'sceneVideoTerminalFailureReconciliation')))
              ))
        THEN 1
    WHEN EXISTS (SELECT 1 FROM render.render_jobs j
        WHERE j.tenant_id=p.tenant_id AND j.job_type='render_scene_video'
          AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id')='814'
          -- A manual-review marker only holds the scene while its own job is still open.
          -- Every other manual-review gate in the application reads the marker this way
          -- (the enqueue guard and the active-job lookup both require pending_reconciliation,
          -- and the watchdog only writes the marker onto a pending_reconciliation job).
          -- A marker left on an already terminated job is history, not a live provider task,
          -- so it must not lock the scene against a brand-new user-initiated operation.
          AND (j.status='pending_reconciliation'
            AND NOT (j.id <> '00000000-0000-0000-0000-000000000000'::uuid
              AND j.error_code='RVIDEO_JOB_VERSION_OWNERSHIP_REQUIRED'
              AND NULLIF(btrim(j.provider_task_id), '') IS NULL
              AND NULLIF(btrim(j.provider_video_id_base), '') IS NULL
              AND (j.lock_until IS NULL OR j.lock_until <= now())
              AND NOT EXISTS (SELECT 1 FROM video_render.scene_video_versions owned_version
                  WHERE owned_version.tenant_id=j.tenant_id AND owned_version.render_job_id=j.id)
              AND NOT EXISTS (SELECT 1 FROM render.scene_video_submit_intents owned_intent
                  WHERE owned_intent.tenant_id=j.tenant_id AND owned_intent.render_job_id=j.id))
            OR (j.status NOT IN ('completed', 'failed', 'cancelled')
                AND (j.error_code='SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
                     OR COALESCE(j.input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt'))
            OR (j.status NOT IN ('completed', 'failed') AND
                (NULLIF(btrim(j.provider_task_id), '') IS NOT NULL
                 OR NULLIF(btrim(j.provider_video_id_base), '') IS NOT NULL))))
        THEN 2
    WHEN EXISTS (SELECT 1 FROM video_render.scene_video_versions v
        WHERE v.tenant_id=p.tenant_id AND v.scene_id=814
          AND ((v.status NOT IN ('completed', 'failed') AND
                (NULLIF(btrim(v.provider_task_id), '') IS NOT NULL
                 OR NULLIF(btrim(v.provider_video_id_base), '') IS NOT NULL))
            OR v.status='pending_reconciliation'))
        THEN 3
    ELSE 0
END AS block_reason
) AS g;
COMMIT;

-- STOP unless an operator can identify the exact tenant, V9 UUID/logical request, intent owner_token
-- and job updated_at, and attach a saved 79AI terminal FAILURE response or provider support's explicit
-- irreversible terminal-failure confirmation for BOTH c6ddf269339eed5a / d6556aa42242fc37.
-- Missing/404, empty output, NOT_RESOURCES, network errors, no progress or expired lease are insufficient.
-- Operator must also fence/drain the OLD worker for this job; an expired DB lease alone cannot stop it.
