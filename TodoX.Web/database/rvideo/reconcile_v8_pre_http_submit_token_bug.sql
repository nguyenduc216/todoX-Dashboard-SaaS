-- ONE-TIME OPERATOR RECONCILIATION. Do not run without written operator approval
-- that the specified job did not send any HTTP request to 79AI.
-- DBeaver: execute the entire file as a script against the approved production
-- database, with auto-commit OFF. Run the separate read-only preflight first;
-- verify the values below against the current rows and independent no-HTTP evidence.
-- Never run this file as a preflight. On any error, explicitly ROLLBACK the session.
-- This transaction does not alter the selected/completed V2 or any scene media pointer.
-- Pre-3b9ca718 V8 could set submitted_at without invoking adapter.SubmitAsync.
-- Only the exact operator-verified V8 timestamp below is permitted; it is not
-- evidence of HTTP by itself. Any provider/HTTP evidence still aborts this transaction.
BEGIN;
SET LOCAL lock_timeout = '5s';
CREATE TABLE IF NOT EXISTS render.scene_video_operator_reconciliations (
    tenant_id uuid NOT NULL,
    render_job_id uuid NOT NULL,
    project_id bigint NOT NULL,
    scene_id bigint NOT NULL,
    version_id uuid NOT NULL,
    logical_request_id text NOT NULL,
    reason_code text NOT NULL,
    confirmed_by text NOT NULL,
    confirmed_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, render_job_id)
);
CREATE TEMP TABLE pre_http_operator_input (tenant_id uuid NOT NULL, confirmed_by text NOT NULL,
    version_id uuid NOT NULL, logical_request_id text NOT NULL, version_error_code text NOT NULL) ON COMMIT DROP;
INSERT INTO pre_http_operator_input VALUES (
    '95f5db58-4233-4dfe-9747-5276105af12a'::uuid, 'admin',
    '7c744095-f0fa-46b6-adf9-2759cc6b39da'::uuid,
    'render_job_scene_video-job-6dc96f9eec6745bdaa2a611996391bc4-scene-814',
    'missing_video_id_base');
SELECT pg_advisory_xact_lock(hashtextextended('render.render_jobs:render_scene_video:scene:814', 0));

DO $reconcile$
DECLARE
    t uuid := (SELECT tenant_id FROM pre_http_operator_input);
    operator_name text := (SELECT confirmed_by FROM pre_http_operator_input);
    v8_target_job_uuid uuid := 'd555306e-9c55-4552-9896-113a1334bfa2';
    v_id uuid;
    logical_id text;
    j record;
    v record;
    i record;
BEGIN
    IF length(btrim(operator_name)) = 0 OR t IS NULL
       OR length(btrim((SELECT logical_request_id FROM pre_http_operator_input))) = 0
       OR length(btrim((SELECT version_error_code FROM pre_http_operator_input))) = 0 THEN
        RAISE EXCEPTION 'Operator identity and tenant are mandatory';
    END IF;

    -- Match the application's scene-level serialization order; lock the scene before job/version.
    PERFORM 1 FROM video_render.video_project_scenes s
        JOIN video_render.video_projects p ON p.id=s.project_id AND p.tenant_id=s.tenant_id
        WHERE s.tenant_id=t AND s.project_id=183 AND s.id=814 FOR UPDATE OF s;
    IF NOT FOUND THEN RAISE EXCEPTION 'Project/scene/tenant mismatch'; END IF;

    SELECT * INTO STRICT j FROM render.render_jobs
        WHERE id=v8_target_job_uuid AND tenant_id=t AND job_type='render_scene_video' FOR UPDATE;
    logical_id := COALESCE(j.input_json->>'logicalRequestId',j.input_json->>'logical_request_id');
    IF j.status IS DISTINCT FROM 'pending_reconciliation'
       OR COALESCE(j.input_json->>'projectId',j.input_json->>'project_id') IS DISTINCT FROM '183'
       OR COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id') IS DISTINCT FROM '814'
       OR logical_id IS DISTINCT FROM (SELECT logical_request_id FROM pre_http_operator_input)
       OR NULLIF(btrim(j.provider_task_id),'') IS NOT NULL OR NULLIF(btrim(j.provider_video_id_base),'') IS NOT NULL
       OR (j.lock_until IS NOT NULL AND j.lock_until > now())
       OR (NULLIF(btrim(j.lock_owner),'') IS NOT NULL AND j.lock_until IS NULL)
       OR j.error_code IS DISTINCT FROM 'SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
    THEN RAISE EXCEPTION 'V8 job preflight mismatch or live lease/provider identity'; END IF;

    SELECT * INTO STRICT v FROM video_render.scene_video_versions
        WHERE tenant_id=t AND project_id=183 AND scene_id=814 AND render_job_id=v8_target_job_uuid
          AND logical_request_id=logical_id FOR UPDATE;
    v_id := v.id;
    IF v.id IS DISTINCT FROM (SELECT version_id FROM pre_http_operator_input)
       OR v.id IS DISTINCT FROM '7c744095-f0fa-46b6-adf9-2759cc6b39da'::uuid
       OR v.status IS DISTINCT FROM 'pending_reconciliation'
       OR v.error_code IS DISTINCT FROM (SELECT version_error_code FROM pre_http_operator_input)
       OR NULLIF(btrim(v.provider_task_id),'') IS NOT NULL OR NULLIF(btrim(v.provider_video_id_base),'') IS NOT NULL
       OR v.submitted_at IS DISTINCT FROM '2026-10-22 00:50:42.110+07'::timestamptz
       OR v.is_selected IS TRUE
    THEN RAISE EXCEPTION 'V8 version preflight mismatch or selected/provider identity'; END IF;

    SELECT * INTO STRICT i FROM render.scene_video_submit_intents
        WHERE tenant_id=t AND scene_id=814 AND render_job_id=v8_target_job_uuid
          AND logical_request_id=logical_id FOR UPDATE;
    IF i.submit_state IS DISTINCT FROM 'SUBMIT_INTENT_PERSISTED'
       OR NULLIF(btrim(i.provider_task_id),'') IS NOT NULL OR NULLIF(btrim(i.provider_video_id_base),'') IS NOT NULL
    THEN RAISE EXCEPTION 'V8 intent progressed beyond pre-HTTP'; END IF;

    IF EXISTS (SELECT 1 FROM render.render_job_events e
        WHERE e.tenant_id=t AND e.job_id=v8_target_job_uuid
          AND (e.event_type LIKE 'RVIDEO_VIDEO_HTTP_SUBMIT_%'
               OR e.event_type LIKE 'RVIDEO_VIDEO_SUBMIT%'))
       OR EXISTS (SELECT 1 FROM video_render.scene_video_versions x
            WHERE x.tenant_id=t AND x.render_job_id=v8_target_job_uuid AND x.id<>v_id)
       OR EXISTS (SELECT 1 FROM render.scene_video_submit_intents x
            WHERE x.tenant_id=t AND x.render_job_id=v8_target_job_uuid
             AND (x.scene_id<>814 OR x.logical_request_id<>logical_id))
       OR EXISTS (SELECT 1 FROM video_render.scene_video_versions x WHERE x.id=v_id
           AND (x.provider_task_id IS NOT NULL OR x.provider_video_id_base IS NOT NULL
                 OR x.completed_at IS NOT NULL
                OR x.result_media_id IS NOT NULL OR x.public_url IS NOT NULL))
       OR EXISTS (SELECT 1 FROM render.scene_video_submit_intents x
            WHERE x.tenant_id=t AND x.render_job_id=v8_target_job_uuid
             AND (x.provider_task_id IS NOT NULL OR x.provider_video_id_base IS NOT NULL
                  OR x.reconciled_at IS NOT NULL))
        OR EXISTS (SELECT 1 FROM render.render_job_events e WHERE e.tenant_id=t AND e.job_id=v8_target_job_uuid
           AND (e.data_json::text ~* 'providerTaskId|providerVideoIdBase|providerRequestSent[^,}]*true'))
       OR EXISTS (SELECT 1 FROM render.render_jobs x
            WHERE x.tenant_id=t AND x.id<>v8_target_job_uuid AND x.job_type='render_scene_video'
             AND COALESCE(x.input_json->>'projectId',x.input_json->>'project_id')='183'
             AND COALESCE(x.input_json->>'sceneId',x.input_json->>'scene_id')='814'
              AND (x.status IN ('queued','preparing','rendering','processing','post_processing','pending_reconciliation')
                   AND NOT (x.id='4190c02c-5279-4534-8ec6-95fbcbbf05b9'
                       AND x.status='pending_reconciliation'
                       AND x.error_code='RVIDEO_JOB_VERSION_OWNERSHIP_REQUIRED'
                       AND NULLIF(btrim(x.provider_task_id),'') IS NULL
                       AND NULLIF(btrim(x.provider_video_id_base),'') IS NULL
                       AND (x.lock_until IS NULL OR x.lock_until<=now())
                       AND NOT EXISTS (SELECT 1 FROM video_render.scene_video_versions ov
                           WHERE ov.tenant_id=t AND ov.render_job_id=x.id)
                       AND NOT EXISTS (SELECT 1 FROM render.scene_video_submit_intents oi
                           WHERE oi.tenant_id=t AND oi.render_job_id=x.id))
                   OR (NULLIF(btrim(x.provider_task_id),'') IS NOT NULL
                       OR NULLIF(btrim(x.provider_video_id_base),'') IS NOT NULL)
                     AND x.status NOT IN ('completed','failed','cancelled')))
       OR EXISTS (SELECT 1 FROM render.scene_video_submit_intents x
            WHERE x.tenant_id=t AND x.scene_id=814 AND x.render_job_id<>v8_target_job_uuid
             AND x.submit_state IN ('SUBMIT_IN_FLIGHT','AMBIGUOUS_SUBMIT','MANUAL_REVIEW_REQUIRED'))
       OR EXISTS (SELECT 1 FROM video_render.scene_video_versions x
           WHERE x.tenant_id=t AND x.scene_id=814 AND x.id<>v_id
              AND (x.status='pending_reconciliation'
                   OR (x.status NOT IN ('completed','failed','cancelled')
                       AND (NULLIF(btrim(x.provider_task_id),'') IS NOT NULL
                            OR NULLIF(btrim(x.provider_video_id_base),'') IS NOT NULL))))
    THEN RAISE EXCEPTION 'HTTP evidence or another unresolved operation exists'; END IF;

    -- Audit is independent of the historical intent/job/version and immutable to this script.
    INSERT INTO render.scene_video_operator_reconciliations
        (tenant_id,render_job_id,project_id,scene_id,version_id,logical_request_id,reason_code,confirmed_by)
    VALUES (t,v8_target_job_uuid,183,814,v_id,logical_id,'PRE_HTTP_SUBMIT_TOKEN_BUG',operator_name);

    UPDATE render.scene_video_submit_intents
       SET submit_state='CONFIRMED_NO_TASK', reconciled_at=now(), updated_at=now()
     WHERE tenant_id=t AND scene_id=814 AND render_job_id=v8_target_job_uuid
       AND logical_request_id=logical_id AND submit_state='SUBMIT_INTENT_PERSISTED';
    IF NOT FOUND THEN RAISE EXCEPTION 'Intent changed concurrently'; END IF;
    UPDATE video_render.scene_video_versions
       SET status='failed', error_code='PRE_HTTP_SUBMIT_TOKEN_BUG',
           error_message='Operator confirmed no provider HTTP submit; see operator reconciliation audit.',
           updated_at=now()
     WHERE id=v_id AND tenant_id=t AND status='pending_reconciliation';
    IF NOT FOUND THEN RAISE EXCEPTION 'Version changed concurrently'; END IF;
    UPDATE render.render_jobs
       SET status='failed', error_code='PRE_HTTP_SUBMIT_TOKEN_BUG',
           error_message='Operator confirmed no provider HTTP submit; see operator reconciliation audit.',
           completed_at=now(), lock_owner=NULL, lock_until=NULL, worker_key=NULL, updated_at=now()
     WHERE id=v8_target_job_uuid AND tenant_id=t AND status='pending_reconciliation';
    IF NOT FOUND THEN RAISE EXCEPTION 'Job changed concurrently'; END IF;

    IF (SELECT status FROM render.render_jobs WHERE id=v8_target_job_uuid) <> 'failed'
       OR (SELECT status FROM video_render.scene_video_versions WHERE id=v_id) <> 'failed'
       OR (SELECT submit_state FROM render.scene_video_submit_intents
           WHERE tenant_id=t AND scene_id=814 AND logical_request_id=logical_id) <> 'CONFIRMED_NO_TASK'
       OR EXISTS (SELECT 1 FROM render.render_jobs x WHERE x.tenant_id=t
           AND x.job_type='render_scene_video'
           AND COALESCE(x.input_json->>'sceneId',x.input_json->>'scene_id')='814'
            AND x.status IN ('queued','preparing','rendering','processing','post_processing','pending_reconciliation')
            AND NOT (x.id='4190c02c-5279-4534-8ec6-95fbcbbf05b9'
                AND x.status='pending_reconciliation' AND x.error_code='RVIDEO_JOB_VERSION_OWNERSHIP_REQUIRED'
                AND NULLIF(btrim(x.provider_task_id),'') IS NULL AND NULLIF(btrim(x.provider_video_id_base),'') IS NULL
                AND (x.lock_until IS NULL OR x.lock_until<=now())
                AND NOT EXISTS (SELECT 1 FROM video_render.scene_video_versions ov WHERE ov.tenant_id=t AND ov.render_job_id=x.id)
                AND NOT EXISTS (SELECT 1 FROM render.scene_video_submit_intents oi WHERE oi.tenant_id=t AND oi.render_job_id=x.id)))
       OR EXISTS (SELECT 1 FROM video_render.scene_video_versions x WHERE x.tenant_id=t
            AND x.scene_id=814 AND (x.status='pending_reconciliation'
                OR (x.status NOT IN ('completed','failed','cancelled')
                    AND (NULLIF(btrim(x.provider_task_id),'') IS NOT NULL
                         OR NULLIF(btrim(x.provider_video_id_base),'') IS NOT NULL))))
    THEN RAISE EXCEPTION 'Postcheck: scene still blocked'; END IF;
END;
$reconcile$;
COMMIT;