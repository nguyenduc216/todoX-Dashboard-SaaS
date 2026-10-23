-- READ ONLY. DBeaver: execute the entire script after COMMIT of the approved reconciliation.
-- FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER; CHANGE_ID: TODOX-20261022-SCENE05-RELEASE-002.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT j.id,j.tenant_id,j.status,j.error_code,j.provider_task_id,j.provider_video_id_base,
       j.retry_after,j.lock_owner,j.worker_key,j.lock_until,
       j.input_json->'sceneVideoTerminalFailureReconciliation' AS resolution,
       j.status='failed' AND j.retry_after IS NULL AND j.lock_owner IS NULL
          AND j.worker_key IS NULL AND j.lock_until IS NULL AS old_job_settled
FROM render.render_jobs j WHERE j.id='f991604f-c72e-4b4e-8c4d-71d232e854f9'::uuid;
SELECT i.submit_state,i.provider_task_id,i.provider_video_id_base,i.owner_token,i.reconciled_at,
       e.created_at AS audited_at,
       e.data_json->'reconciliation'=j.input_json->'sceneVideoTerminalFailureReconciliation' AS matching_audit,
       (e.data_json->'selectedV2Before') IS NOT DISTINCT FROM to_jsonb(v2) AS selected_v2_unchanged,
       (e.data_json->'sceneBefore') IS NOT DISTINCT FROM to_jsonb(s) AS scene_unchanged,
       (e.data_json->'versionsBefore') IS NOT DISTINCT FROM
           (SELECT jsonb_agg(to_jsonb(v) ORDER BY v.id) FROM video_render.scene_video_versions v
            WHERE v.tenant_id=j.tenant_id AND v.scene_id=814) AS all_versions_unchanged
FROM render.render_jobs j
JOIN render.scene_video_submit_intents i ON i.render_job_id=j.id AND i.tenant_id=j.tenant_id
 AND i.provider_task_id='c6ddf269339eed5a' AND i.provider_video_id_base='d6556aa42242fc37'
JOIN render.render_job_events e ON e.job_id=j.id AND e.tenant_id=j.tenant_id
 AND e.event_type='SCENE05_OPERATOR_TERMINAL_FAILURE_RECONCILED'
JOIN video_render.video_project_scenes s ON s.id=814 AND s.project_id=183 AND s.tenant_id=j.tenant_id
JOIN video_render.scene_video_versions v2 ON v2.id=s.selected_video_version_id AND v2.tenant_id=j.tenant_id
WHERE j.id='f991604f-c72e-4b4e-8c4d-71d232e854f9'::uuid;

-- The same shared guard evaluated for a NEW operation. Expect block_reason=0.
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
SELECT j.id,j.status,j.provider_task_id,j.provider_video_id_base
FROM render.render_jobs j
WHERE j.tenant_id=(SELECT tenant_id FROM render.render_jobs WHERE id='f991604f-c72e-4b4e-8c4d-71d232e854f9'::uuid)
 AND j.job_type='render_scene_video' AND COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id')='814'
 AND j.status IN ('queued','preparing','rendering','processing','post_processing','pending_reconciliation');
-- Expect ZERO active scene jobs, one matching audit, unchanged V2/scene/versions and block_reason=0.
-- Refresh UI against the matching backend release. Latest render = Failed; selected video = V2 Completed.
-- Render again may be enabled only with valid image, motion prompt, aspect/resolution/provider configuration.
COMMIT;
