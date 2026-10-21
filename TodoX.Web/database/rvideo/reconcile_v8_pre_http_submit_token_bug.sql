-- ONE-TIME OPERATOR RECONCILIATION. Do not run without written operator approval
-- that the specified job did not send any HTTP request to 79AI.
-- psql: set the REAL tenant UUID and operator identity before executing:
--   \set tenant_id '...'
--   \set confirmed_by '...'
--   \set expected_version_id '...'  -- from read-only preflight
--   \set expected_logical_request_id '...'  -- from read-only preflight
--   \set expected_version_error_code '...'  -- from read-only preflight
-- Execute with psql -v ON_ERROR_STOP=1 and ALL variables above; run the separate
-- read-only preflight first. Never run this file as a preflight.
-- This transaction does not alter the selected/completed V2 or any scene media pointer.
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
INSERT INTO pre_http_operator_input VALUES (:'tenant_id'::uuid, :'confirmed_by',
    :'expected_version_id'::uuid, :'expected_logical_request_id', :'expected_version_error_code');
SELECT pg_advisory_xact_lock(hashtextextended('render.render_jobs:render_scene_video:scene:814', 0));

DO $reconcile$
DECLARE
    t uuid := (SELECT tenant_id FROM pre_http_operator_input);
    operator_name text := (SELECT confirmed_by FROM pre_http_operator_input);
    job_id uuid := 'd555306e-9c55-4552-9896-113a1334bfa2';
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
        WHERE id=job_id AND tenant_id=t AND job_type='render_scene_video' FOR UPDATE;
    logical_id := COALESCE(j.input_json->>'logicalRequestId',j.input_json->>'logical_request_id');
    IF j.status IS DISTINCT FROM 'pending_reconciliation'
       OR COALESCE(j.input_json->>'projectId',j.input_json->>'project_id') IS DISTINCT FROM '183'
       OR COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id') IS DISTINCT FROM '814'
       OR logical_id IS DISTINCT FROM (SELECT logical_request_id FROM pre_http_operator_input)
       OR NULLIF(btrim(j.provider_task_id),'') IS NOT NULL OR NULLIF(btrim(j.provider_video_id_base),'') IS NOT NULL
       OR (j.lock_until IS NOT NULL AND j.lock_until > now())
       OR (NULLIF(btrim(j.lock_owner),'') IS NOT NULL AND j.lock_until IS NULL)
       OR j.error_code IS NOT DISTINCT FROM 'SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
       OR COALESCE(j.input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt'
    THEN RAISE EXCEPTION 'V8 job preflight mismatch or live lease/provider identity'; END IF;

    SELECT * INTO STRICT v FROM video_render.scene_video_versions
        WHERE tenant_id=t AND project_id=183 AND scene_id=814 AND render_job_id=job_id
          AND logical_request_id=logical_id FOR UPDATE;
    v_id := v.id;
    IF v.id IS DISTINCT FROM (SELECT version_id FROM pre_http_operator_input)
       OR v.status IS DISTINCT FROM 'pending_reconciliation'
       OR v.error_code IS DISTINCT FROM (SELECT version_error_code FROM pre_http_operator_input)
       OR NULLIF(btrim(v.provider_task_id),'') IS NOT NULL OR NULLIF(btrim(v.provider_video_id_base),'') IS NOT NULL
       OR v.is_selected IS TRUE
    THEN RAISE EXCEPTION 'V8 version preflight mismatch or selected/provider identity'; END IF;

    SELECT * INTO STRICT i FROM render.scene_video_submit_intents
        WHERE tenant_id=t AND scene_id=814 AND render_job_id=job_id
          AND logical_request_id=logical_id FOR UPDATE;
    IF i.submit_state IS DISTINCT FROM 'SUBMIT_INTENT_PERSISTED'
       OR NULLIF(btrim(i.provider_task_id),'') IS NOT NULL OR NULLIF(btrim(i.provider_video_id_base),'') IS NOT NULL
    THEN RAISE EXCEPTION 'V8 intent progressed beyond pre-HTTP'; END IF;

    IF EXISTS (SELECT 1 FROM render.render_job_events e
        WHERE e.tenant_id=t AND e.job_id=job_id
          AND (e.event_type LIKE 'RVIDEO_VIDEO_HTTP_SUBMIT_%'
               OR e.event_type LIKE 'RVIDEO_VIDEO_SUBMIT%'))
       OR EXISTS (SELECT 1 FROM video_render.scene_video_versions x
           WHERE x.tenant_id=t AND x.render_job_id=job_id AND x.id<>v_id)
       OR EXISTS (SELECT 1 FROM render.scene_video_submit_intents x
           WHERE x.tenant_id=t AND x.render_job_id=job_id
             AND (x.scene_id<>814 OR x.logical_request_id<>logical_id))
       OR EXISTS (SELECT 1 FROM video_render.scene_video_versions x WHERE x.id=v_id
           AND (x.provider_task_id IS NOT NULL OR x.provider_video_id_base IS NOT NULL
                OR x.submitted_at IS NOT NULL OR x.completed_at IS NOT NULL
                OR x.result_media_id IS NOT NULL OR x.public_url IS NOT NULL))
       OR EXISTS (SELECT 1 FROM render.scene_video_submit_intents x
           WHERE x.tenant_id=t AND x.render_job_id=job_id
             AND (x.provider_task_id IS NOT NULL OR x.provider_video_id_base IS NOT NULL
                  OR x.reconciled_at IS NOT NULL))
       OR EXISTS (SELECT 1 FROM render.render_job_events e WHERE e.tenant_id=t AND e.job_id=job_id
           AND (e.data_json::text ~* 'providerTaskId|providerVideoIdBase|providerRequestSent[^,}]*true'))
       OR EXISTS (SELECT 1 FROM render.render_jobs x
           WHERE x.tenant_id=t AND x.id<>job_id AND x.job_type='render_scene_video'
             AND COALESCE(x.input_json->>'projectId',x.input_json->>'project_id')='183'
             AND COALESCE(x.input_json->>'sceneId',x.input_json->>'scene_id')='814'
             AND (x.status IN ('queued','preparing','rendering','processing','post_processing','pending_reconciliation')
                  OR NULLIF(btrim(x.provider_task_id),'') IS NOT NULL
                     AND x.status NOT IN ('completed','failed','cancelled')))
       OR EXISTS (SELECT 1 FROM render.scene_video_submit_intents x
           WHERE x.tenant_id=t AND x.scene_id=814 AND x.render_job_id<>job_id
             AND x.submit_state IN ('SUBMIT_IN_FLIGHT','AMBIGUOUS_SUBMIT','MANUAL_REVIEW_REQUIRED'))
       OR EXISTS (SELECT 1 FROM video_render.scene_video_versions x
           WHERE x.tenant_id=t AND x.scene_id=814 AND x.id<>v_id
             AND (x.status NOT IN ('completed','failed','cancelled')
                  OR NULLIF(btrim(x.provider_task_id),'') IS NOT NULL AND x.status <> 'completed'))
    THEN RAISE EXCEPTION 'HTTP evidence or another unresolved operation exists'; END IF;

    -- Audit is independent of the historical intent/job/version and immutable to this script.
    INSERT INTO render.scene_video_operator_reconciliations
        (tenant_id,render_job_id,project_id,scene_id,version_id,logical_request_id,reason_code,confirmed_by)
    VALUES (t,job_id,183,814,v_id,logical_id,'PRE_HTTP_SUBMIT_TOKEN_BUG',operator_name);

    UPDATE render.scene_video_submit_intents
       SET submit_state='CONFIRMED_NO_TASK', reconciled_at=now(), updated_at=now()
     WHERE tenant_id=t AND scene_id=814 AND render_job_id=job_id
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
     WHERE id=job_id AND tenant_id=t AND status='pending_reconciliation';
    IF NOT FOUND THEN RAISE EXCEPTION 'Job changed concurrently'; END IF;

    IF (SELECT status FROM render.render_jobs WHERE id=job_id) <> 'failed'
       OR (SELECT status FROM video_render.scene_video_versions WHERE id=v_id) <> 'failed'
       OR (SELECT submit_state FROM render.scene_video_submit_intents
           WHERE tenant_id=t AND scene_id=814 AND logical_request_id=logical_id) <> 'CONFIRMED_NO_TASK'
       OR EXISTS (SELECT 1 FROM render.render_jobs x WHERE x.tenant_id=t
           AND x.job_type='render_scene_video'
           AND COALESCE(x.input_json->>'sceneId',x.input_json->>'scene_id')='814'
           AND x.status IN ('queued','preparing','rendering','processing','post_processing','pending_reconciliation'))
       OR EXISTS (SELECT 1 FROM video_render.scene_video_versions x WHERE x.tenant_id=t
           AND x.scene_id=814 AND x.status IN ('queued','submitted','processing','pending_reconciliation'))
    THEN RAISE EXCEPTION 'Postcheck: scene still blocked'; END IF;
END;
$reconcile$;
COMMIT;