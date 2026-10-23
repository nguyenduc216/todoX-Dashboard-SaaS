-- OPERATOR ONLY. Never run unattended. DBeaver: edit inputs, then execute the ENTIRE script.
-- FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER; CHANGE_ID: TODOX-20261022-SCENE05-RELEASE-002.
-- Exactly Project 183 / Scene 814 / job f991604f...; no schema change, provider call or billing change.
-- Deploy/review the matching audited-terminal-failure Submit Guard code before using this resolution.
-- Defaults deliberately RAISE EXCEPTION. On error, issue ROLLBACK; investigate; do not bypass guards.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='60s';
DO $scene05$
DECLARE
    -- Fill from the read-only preflight and the external evidence reviewed by the approving operator.
    v_approved boolean := false;
    v_old_worker_fenced boolean := false;
    v_operator text := NULL;
    v_approval_reference text := NULL;
    v_tenant uuid := NULL;
    v_owner_version uuid := NULL;                 -- exact V9 UUID
    v_owner_logical_request text := NULL;
    v_intent_token uuid := NULL;
    v_expected_job_updated_at timestamptz := NULL;
    v_evidence_reference text := NULL;            -- saved response/log/support record, no credentials
    v_observed_at timestamptz := NULL;
    -- Must be a reviewed JSON object with exact identities and normalizedStatus=FAILED,
    -- terminal=true, rawResponse=<nonempty sanitized provider response/support confirmation object>.
    v_evidence jsonb := '{}'::jsonb;

    c_job constant uuid := 'f991604f-c72e-4b4e-8c4d-71d232e854f9';
    c_task constant text := 'c6ddf269339eed5a';
    c_base constant text := 'd6556aa42242fc37';
    c_event constant text := 'SCENE05_OPERATOR_TERMINAL_FAILURE_RECONCILED';
    j render.render_jobs%ROWTYPE;
    own video_render.scene_video_versions%ROWTYPE;
    intent render.scene_video_submit_intents%ROWTYPE;
    selected_before jsonb;
    scene_before jsonb;
    versions_before jsonb;
    intent_before jsonb;
    job_before jsonb;
    resolution jsonb;
    audit_payload jsonb;
    row_count integer;
    blocker integer;
BEGIN
    IF NOT v_approved OR NOT v_old_worker_fenced OR NULLIF(btrim(v_operator),'') IS NULL
       OR NULLIF(btrim(v_approval_reference),'') IS NULL OR v_tenant IS NULL
       OR v_owner_version IS NULL OR v_intent_token IS NULL OR v_expected_job_updated_at IS NULL
       OR NULLIF(btrim(v_owner_logical_request),'') IS NULL OR NULLIF(btrim(v_evidence_reference),'') IS NULL
       OR v_observed_at IS NULL OR v_observed_at > clock_timestamp() THEN
        RAISE EXCEPTION 'STOP: operator approval, worker fencing, exact preflight identities/time and terminal evidence are required';
    END IF;
    IF jsonb_typeof(v_evidence) IS DISTINCT FROM 'object'
       OR (v_evidence->>'providerTaskId') IS DISTINCT FROM c_task
       OR (v_evidence->>'providerVideoIdBase') IS DISTINCT FROM c_base
       OR (v_evidence->>'normalizedStatus') IS DISTINCT FROM 'FAILED'
       OR (v_evidence->>'terminal') IS DISTINCT FROM 'true'
       OR jsonb_typeof(v_evidence->'rawResponse') IS DISTINCT FROM 'object'
       OR v_evidence->'rawResponse' = '{}'::jsonb THEN
        RAISE EXCEPTION 'STOP: no reviewed terminal FAILED provider evidence for the exact task/video base';
    END IF;

    -- Same lock ordering as scene writers: scene advisory -> scene row -> versions -> job/intent.
    PERFORM pg_advisory_xact_lock(hashtextextended('render.render_jobs:render_scene_video:scene:814',0));
    PERFORM 1 FROM video_render.video_project_scenes
     WHERE id=814 AND project_id=183 AND tenant_id=v_tenant FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'Scene/tenant/project mismatch'; END IF;
    PERFORM 1 FROM video_render.scene_video_versions WHERE scene_id=814 AND tenant_id=v_tenant ORDER BY id FOR UPDATE;
    SELECT * INTO STRICT j FROM render.render_jobs WHERE id=c_job AND tenant_id=v_tenant FOR UPDATE;
    SELECT * INTO STRICT own FROM video_render.scene_video_versions WHERE id=v_owner_version AND tenant_id=v_tenant;
    SELECT * INTO STRICT intent FROM render.scene_video_submit_intents
     WHERE tenant_id=v_tenant AND scene_id=814 AND logical_request_id=v_owner_logical_request FOR UPDATE;

    IF j.job_type <> 'render_scene_video' OR j.input_json->>'projectId' IS DISTINCT FROM '183'
       OR j.input_json->>'sceneId' IS DISTINCT FROM '814'
       OR j.provider_task_id IS DISTINCT FROM c_task OR j.provider_video_id_base IS DISTINCT FROM c_base
       OR own.project_id <> 183 OR own.scene_id <> 814 OR own.version_number <> 9
       OR own.render_job_id IS DISTINCT FROM c_job OR own.logical_request_id IS DISTINCT FROM v_owner_logical_request
       OR own.provider_task_id IS DISTINCT FROM c_task OR own.provider_video_id_base IS DISTINCT FROM c_base
       OR own.status <> 'failed' OR own.is_selected
       OR intent.render_job_id <> c_job OR intent.owner_token <> v_intent_token
       OR intent.submit_state <> 'CONFIRMED_PROVIDER_TASK'
       OR intent.provider_task_id IS DISTINCT FROM c_task OR intent.provider_video_id_base IS DISTINCT FROM c_base
       OR NULLIF(j.input_json->>'logicalRequestId','') IS NULL
       OR NOT (own.logical_request_id=j.input_json->>'logicalRequestId'
           OR left(own.logical_request_id,length(j.input_json->>'logicalRequestId')+10)
                =(j.input_json->>'logicalRequestId')||'-fallback-') THEN
        RAISE EXCEPTION 'Owner/job/logical request/confirmed intent/provider identity does not match the approved V9';
    END IF;

    SELECT to_jsonb(v) INTO STRICT selected_before FROM video_render.scene_video_versions v
     WHERE tenant_id=v_tenant AND project_id=183 AND scene_id=814 AND version_number=2 AND is_selected AND status='completed';
    SELECT to_jsonb(s) INTO scene_before FROM video_render.video_project_scenes s
     WHERE id=814 AND tenant_id=v_tenant AND selected_video_version_id=(selected_before->>'id')::uuid;
    IF scene_before IS NULL OR (SELECT count(*) FROM video_render.scene_video_versions
        WHERE tenant_id=v_tenant AND scene_id=814 AND is_selected) <> 1 THEN
        RAISE EXCEPTION 'Expected V2 completed and uniquely selected; do not modify selection';
    END IF;
    IF (SELECT count(*) FROM video_render.scene_video_versions
        WHERE tenant_id=v_tenant AND scene_id=814 AND project_id=183 AND version_number=10 AND status='failed' AND NOT is_selected) <> 1
       OR (SELECT count(*) FROM video_render.scene_video_versions
        WHERE tenant_id=v_tenant AND (provider_task_id=c_task OR provider_video_id_base=c_base)) <> 1
       OR (SELECT count(*) FROM render.scene_video_submit_intents
        WHERE tenant_id=v_tenant AND (provider_task_id=c_task OR provider_video_id_base=c_base)) <> 1 THEN
        RAISE EXCEPTION 'V10 changed, or provider identity has duplicate/conflicting owners';
    END IF;
    IF EXISTS (SELECT 1 FROM video_render.scene_video_versions WHERE tenant_id=v_tenant AND render_job_id=c_job
            AND (status <> 'failed' OR is_selected OR version_number NOT IN (9,10))) THEN
        RAISE EXCEPTION 'Job has an unexpected/nonfailed/selected version; reconcile separately';
    END IF;
    IF j.input_json ? 'sceneVideoTerminalFailureReconciliation' THEN
        -- Idempotent only if the same resolution and its immutable audit already exist.
        IF j.status='failed' AND intent.reconciled_at IS NOT NULL
           AND j.input_json->'sceneVideoTerminalFailureReconciliation'->>'resolution'='TERMINAL_FAILED'
           AND j.input_json->'sceneVideoTerminalFailureReconciliation'->>'intentOwnerToken'=v_intent_token::text
           AND j.input_json->'sceneVideoTerminalFailureReconciliation'->>'evidenceReference'=v_evidence_reference
           AND EXISTS (SELECT 1 FROM render.render_job_events e WHERE e.job_id=c_job AND e.tenant_id=v_tenant
                AND e.event_type=c_event AND e.data_json->'reconciliation'=j.input_json->'sceneVideoTerminalFailureReconciliation') THEN
            RAISE NOTICE 'Already reconciled; no update performed'; RETURN;
        END IF;
        RAISE EXCEPTION 'Existing resolution conflicts; no overwrite permitted';
    END IF;
    IF j.status NOT IN ('rendering','processing','queued','preparing','post_processing','pending_reconciliation','failed')
       OR j.updated_at IS DISTINCT FROM v_expected_job_updated_at OR j.lock_until > clock_timestamp() THEN
        RAISE EXCEPTION 'Job changed since preflight or still has a live lease; repeat preflight after worker fencing';
    END IF;
    IF v_observed_at < COALESCE(own.submitted_at,j.created_at) THEN
        RAISE EXCEPTION 'Evidence predates this provider attempt';
    END IF;
    IF EXISTS (SELECT 1 FROM render.render_jobs other WHERE other.tenant_id=v_tenant AND other.id<>c_job
         AND other.job_type='render_scene_video' AND COALESCE(other.input_json->>'sceneId',other.input_json->>'scene_id')='814'
         AND other.status IN ('queued','preparing','rendering','processing','post_processing','pending_reconciliation')) THEN
        RAISE EXCEPTION 'Another active scene job exists; this script cannot settle another operation';
    END IF;

    job_before:=to_jsonb(j); intent_before:=to_jsonb(intent);
    SELECT jsonb_agg(to_jsonb(v) ORDER BY v.id) INTO versions_before FROM video_render.scene_video_versions v WHERE v.tenant_id=v_tenant AND v.scene_id=814;
    resolution:=jsonb_build_object('resolution','TERMINAL_FAILED','jobId',c_job,'projectId',183,'sceneId',814,
        'ownerVersionId',v_owner_version,'logicalRequestId',v_owner_logical_request,'intentOwnerToken',v_intent_token,
        'providerTaskId',c_task,'providerVideoIdBase',c_base,'operatorApproved',true,'operator',v_operator,
        'approvalReference',v_approval_reference,'oldWorkerFenced',true,'evidenceKind','provider_terminal_failure',
        'evidenceReference',v_evidence_reference,'observedAt',v_observed_at,'evidence',v_evidence,
        'reconciledAt',clock_timestamp(),'databaseUser',current_user,'transactionId',txid_current());
    UPDATE render.render_jobs SET status='failed', completed_at=COALESCE(completed_at,now()),
        error_code='SCENE05_PROVIDER_TERMINAL_FAILURE_RECONCILED',
        error_message='Operator confirmed terminal provider failure; prior selected video preserved.',
        retry_after=NULL,lock_owner=NULL,worker_key=NULL,lock_until=NULL,
        input_json=COALESCE(input_json,'{}'::jsonb)||jsonb_build_object('sceneVideoTerminalFailureReconciliation',resolution),updated_at=now()
     WHERE id=c_job AND tenant_id=v_tenant AND updated_at IS NOT DISTINCT FROM v_expected_job_updated_at;
    GET DIAGNOSTICS row_count=ROW_COUNT;
    IF row_count<>1 THEN RAISE EXCEPTION 'Concurrent job modification'; END IF;
    -- Retain the TRUE historical state/IDs: a task existed. Closure is resolution + reconciled_at + audit.
    UPDATE render.scene_video_submit_intents SET reconciled_at=now(),updated_at=now()
     WHERE tenant_id=v_tenant AND owner_token=v_intent_token AND render_job_id=c_job
       AND submit_state='CONFIRMED_PROVIDER_TASK' AND provider_task_id=c_task AND provider_video_id_base=c_base;
    GET DIAGNOSTICS row_count=ROW_COUNT;
    IF row_count<>1 THEN RAISE EXCEPTION 'Concurrent intent modification'; END IF;
    audit_payload:=jsonb_build_object('reconciliation',resolution,'jobBefore',job_before,'intentBefore',intent_before,
        'versionsBefore',versions_before,'selectedV2Before',selected_before,'sceneBefore',scene_before);
    INSERT INTO render.render_job_events(job_id,tenant_id,event_type,level,message,data_json,created_at)
    VALUES(c_job,v_tenant,c_event,'warning','Operator reconciled Scene 05 terminal provider failure without changing video versions.',audit_payload,now());
    INSERT INTO video_render.video_project_events(project_id,tenant_id,event_type,level,message,data_json,created_at)
    VALUES(183,v_tenant,c_event,'warning','Scene 05 old render job settled failed; V2 selected remains unchanged.',audit_payload,now());

    IF selected_before IS DISTINCT FROM (SELECT to_jsonb(v) FROM video_render.scene_video_versions v WHERE id=(selected_before->>'id')::uuid AND tenant_id=v_tenant)
       OR scene_before IS DISTINCT FROM (SELECT to_jsonb(s) FROM video_render.video_project_scenes s WHERE id=814 AND tenant_id=v_tenant)
       OR versions_before IS DISTINCT FROM (SELECT jsonb_agg(to_jsonb(v) ORDER BY v.id) FROM video_render.scene_video_versions v WHERE tenant_id=v_tenant AND scene_id=814) THEN
        RAISE EXCEPTION 'Selection/scene/version changed unexpectedly; entire transaction must roll back';
    END IF;
    -- Exact shared backend guard for a NEW operation; any remaining blocker aborts this transaction.
    SELECT CASE
    WHEN EXISTS (SELECT 1 FROM render.scene_video_submit_intents i
        JOIN render.render_jobs prior ON prior.id=i.render_job_id AND prior.tenant_id=i.tenant_id
        WHERE i.tenant_id=v_tenant AND i.scene_id=814
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
        WHERE j.tenant_id=v_tenant AND j.job_type='render_scene_video'
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
        WHERE v.tenant_id=v_tenant AND v.scene_id=814
          AND ((v.status NOT IN ('completed', 'failed') AND
                (NULLIF(btrim(v.provider_task_id), '') IS NOT NULL
                 OR NULLIF(btrim(v.provider_video_id_base), '') IS NOT NULL))
            OR v.status='pending_reconciliation'))
        THEN 3
    ELSE 0
END INTO blocker;
    IF blocker<>0 THEN RAISE EXCEPTION 'Another backend blocker remains (%); rollback rather than release other jobs',blocker; END IF;
    RAISE NOTICE 'Reconciled exactly job %, kept V2 and all versions unchanged. No provider submit was made.',c_job;
END
$scene05$;
COMMIT;
-- Rollback is deliberately NOT an automatic requeue: that could duplicate provider work.
-- The append-only audit contains before-images. Any reversal requires fresh provider/lease checks and separate approval.
