-- EXACT TARGETS BOUND. EXECUTION DISABLED until supplemental snapshots/schema/evidence/fencing are approved.
-- OPERATOR ONLY. Execute ENTIRE script in DBeaver. On any error, ROLLBACK; never continue selected statements.
-- No schema changes, provider call, auto-render, retry, selection, image or billing mutation.
BEGIN TRANSACTION ISOLATION LEVEL SERIALIZABLE;
SET LOCAL TIME ZONE 'UTC';
SET LOCAL row_security=off; -- fail on hidden rows; does not bypass RLS
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='120s';
DO $project188$
DECLARE
    v_approved boolean := false;
    v_schema_reviewed boolean := false;
    v_expected_database text := 'todo_saas'; -- operator-supplied; not independently queried here
    v_expected_schema_signature text := 'b9284fc184186344f46a7e58e7c558dd'; -- operator-supplied PG 17.10 / role postgres
    v_operator text := NULL;
    v_approval_reference text := NULL;
    v_batch_id uuid := NULL; -- operator-generated unique ID; reuse for read-only postcheck
    -- Reuse the original supplemental_manifest already collected by the operator; do NOT rerun preflight to copy it.
    -- Raw seven-row timestamps/hashes are not visible in Codex's accessible context: never reconstruct them.
    -- Preserve its false/UNREVIEWED approval/evidence fields until each real approval/fence is established.
    -- All seven entries must have approved=true, workerFenced=true,
    -- httpOutcome=CONFIRMED_PRE_HTTP_NO_SUBMIT, noSubmitEvidenceReference,
    -- projectEventsReviewed=true, projectEventsReference, and applicationLogsReference.
    -- applicationLogsStatus is REVIEWED_NO_SUBMIT or UNAVAILABLE (explain why in the reference).
    v_manifest jsonb := '[]'::jsonb;
    c_targets constant jsonb := '[
  {
    "sceneId": 831,
    "jobId": "30663c04-ea85-4738-87ed-a93d2414f694",
    "versionId": "8f3e7daf-d8d4-4d1f-8c23-07dfd4a7cf9d",
    "logicalRequestId": "render_job_scene_video-job-046bcf432ecf4b97995268dfc3f8e9eb-scene-831"
  },
  {
    "sceneId": 832,
    "jobId": "e130accb-4423-445c-893e-46bce98b63e9",
    "versionId": "da22066a-6e44-42e9-9db9-48f31eba2a8e",
    "logicalRequestId": "render_job_scene_video-job-046bcf432ecf4b97995268dfc3f8e9eb-scene-832"
  },
  {
    "sceneId": 833,
    "jobId": "0404a954-793a-4a2d-8cd1-706de24c9ca5",
    "versionId": "6fe5d520-cf51-414d-a664-2dbcff8d900d",
    "logicalRequestId": "render_job_scene_video-job-046bcf432ecf4b97995268dfc3f8e9eb-scene-833"
  },
  {
    "sceneId": 834,
    "jobId": "47d877ea-1ab9-4f28-bec8-9fc5810d45c5",
    "versionId": "ae0866b5-5b02-46b2-8967-efe39f01e46a",
    "logicalRequestId": "render_job_scene_video-job-046bcf432ecf4b97995268dfc3f8e9eb-scene-834"
  },
  {
    "sceneId": 835,
    "jobId": "1f53cedf-0347-4fff-9e42-1347ba8cec60",
    "versionId": "526282e5-00f6-4a80-ab68-2d34a8017745",
    "logicalRequestId": "render_job_scene_video-job-046bcf432ecf4b97995268dfc3f8e9eb-scene-835"
  },
  {
    "sceneId": 836,
    "jobId": "fca7c1b7-5829-495f-90e2-e47f5e074651",
    "versionId": "1ed9070a-c7ee-4c9a-aa4b-7b6836c74b1e",
    "logicalRequestId": "render_job_scene_video-job-046bcf432ecf4b97995268dfc3f8e9eb-scene-836"
  },
  {
    "sceneId": 837,
    "jobId": "236d182e-2c02-4238-8f87-8b9892e7bdf3",
    "versionId": "af52ed9a-a04b-455a-b565-c5b9c130dc6d",
    "logicalRequestId": "render_job_scene_video-job-046bcf432ecf4b97995268dfc3f8e9eb-scene-837"
  }
]'::jsonb;
    c_tenant constant uuid := '95f5db58-4233-4dfe-9747-5276105af12a';
    c_core constant uuid := '2f3664eb-cdf6-44bd-aa6f-8ef12cc8b432';
    c_event constant text := 'PROJECT188_PREHTTP_NO_SUBMIT_RECONCILED';
    c_reason constant text := 'RVIDEO_PREHTTP_DTO_OWNER_RECONCILED';
    c_message constant text := 'Selected version does not belong to this job/scene/logical request; submit is prohibited.';
    op_target record;
    op_job render.render_jobs%ROWTYPE;
    op_version video_render.scene_video_versions%ROWTYPE;
    v_actual_schema text;
    v_schema_details jsonb;
    v_before jsonb := '[]'::jsonb;
    op_snapshot jsonb;
    receipt jsonb;
    n integer;
    blocker integer;
    scene_hash text;
    images_hash text;
    billing_hash text;
    other_versions_hash text;
    project_hash text;
    parent_jobs_hash text;
BEGIN
    IF v_approved IS DISTINCT FROM true OR v_schema_reviewed IS DISTINCT FROM true
       OR v_expected_database IS DISTINCT FROM current_database()
       OR NULLIF(btrim(v_operator),'') IS NULL OR NULLIF(btrim(v_approval_reference),'') IS NULL
       OR v_batch_id IS NULL OR v_expected_schema_signature IS NULL THEN
        RAISE EXCEPTION 'STOP: approval, database/schema review, operator, ticket and unique batch ID required';
    END IF;
    IF jsonb_typeof(v_manifest) IS DISTINCT FROM 'array' THEN RAISE EXCEPTION 'Manifest must be an array'; END IF;
    IF jsonb_array_length(v_manifest)<>7 THEN RAISE EXCEPTION 'Exactly seven approved identities required; no partial batch'; END IF;
    IF EXISTS (SELECT 1 FROM jsonb_array_elements(v_manifest) x WHERE
        NULLIF(x->>'jobId','') IS NULL OR NULLIF(x->>'versionId','') IS NULL OR NULLIF(x->>'sceneId','') IS NULL
        OR NULLIF(x->>'logicalRequestId','') IS NULL
        OR NULLIF(x->>'jobUpdatedAt','') IS NULL OR NULLIF(x->>'versionUpdatedAt','') IS NULL
        OR COALESCE(x->>'jobSnapshotHash','') !~ '^[0-9a-f]{32}$'
        OR COALESCE(x->>'versionSnapshotHash','') !~ '^[0-9a-f]{32}$'
        OR (x->>'approved') IS DISTINCT FROM 'true' OR (x->>'workerFenced') IS DISTINCT FROM 'true'
        OR (x->>'httpOutcome') IS DISTINCT FROM 'CONFIRMED_PRE_HTTP_NO_SUBMIT'
        OR NULLIF(btrim(x->>'noSubmitEvidenceReference'),'') IS NULL
        OR (x->>'projectEventsReviewed') IS DISTINCT FROM 'true'
        OR NULLIF(btrim(x->>'projectEventsReference'),'') IS NULL
        OR COALESCE(x->>'applicationLogsStatus','') NOT IN ('REVIEWED_NO_SUBMIT','UNAVAILABLE')
        OR NULLIF(btrim(x->>'applicationLogsReference'),'') IS NULL) THEN
        RAISE EXCEPTION 'STOP: incomplete per-job identity/snapshot/evidence/worker-fencing approval';
    END IF;
    IF EXISTS (SELECT 1 FROM jsonb_array_elements(v_manifest) m
        WHERE NOT EXISTS (SELECT 1 FROM jsonb_array_elements(c_targets) t
            WHERE t->>'sceneId'=m->>'sceneId' AND t->>'jobId'=m->>'jobId'
              AND t->>'versionId'=m->>'versionId' AND t->>'logicalRequestId'=m->>'logicalRequestId')) THEN
        RAISE EXCEPTION 'STOP: approved manifest differs from the seven supplied immutable UUID pairs';
    END IF;
    IF (SELECT count(DISTINCT x->>'jobId') FROM jsonb_array_elements(v_manifest) x)<>7
       OR (SELECT count(DISTINCT x->>'versionId') FROM jsonb_array_elements(v_manifest) x)<>7
       OR (SELECT count(DISTINCT x->>'logicalRequestId') FROM jsonb_array_elements(v_manifest) x)<>7
       OR (SELECT array_agg((x->>'sceneId')::bigint ORDER BY (x->>'sceneId')::bigint) FROM jsonb_array_elements(v_manifest) x)
            IS DISTINCT FROM ARRAY[831,832,833,834,835,836,837]::bigint[] THEN
        RAISE EXCEPTION 'STOP: duplicate identities or scene set differs from exact 831-837';
    END IF;
WITH relations AS (SELECT unnest(ARRAY['render.render_jobs','render.render_job_events','render.scene_video_submit_intents',
'video_render.video_projects','video_render.video_project_scenes','video_render.scene_video_versions',
'video_render.scene_image_versions','video_render.video_project_events','billing.ai_image_billing_records']) AS name),
cols AS (SELECT r.name,a.attnum,a.attname,format_type(a.atttypid,a.atttypmod) AS type,a.attnotnull,
                   pg_get_expr(d.adbin,d.adrelid) AS default_expr,
                   (SELECT jsonb_agg(e.enumlabel ORDER BY e.enumsortorder) FROM pg_enum e WHERE e.enumtypid=a.atttypid) AS enum_values
 FROM relations r JOIN pg_attribute a ON a.attrelid=to_regclass(r.name) AND a.attnum>0 AND NOT a.attisdropped
 LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum),
cons AS (SELECT r.name,c.conname,pg_get_constraintdef(c.oid) AS definition
 FROM relations r JOIN pg_constraint c ON c.conrelid=to_regclass(r.name)),
trigs AS (SELECT r.name,t.tgname,t.tgenabled,pg_get_triggerdef(t.oid) AS definition,
                md5(pg_get_functiondef(t.tgfoid)) AS function_hash
 FROM relations r JOIN pg_trigger t ON t.tgrelid=to_regclass(r.name) AND NOT t.tgisinternal),
facts AS (SELECT jsonb_build_object(
 'relations',(SELECT jsonb_agg(jsonb_build_object('name',name,'exists',to_regclass(name) IS NOT NULL,
   'selectGranted',has_table_privilege(current_user,to_regclass(name),'SELECT'),
   'updateGranted',has_table_privilege(current_user,to_regclass(name),'UPDATE'),
   'insertGranted',has_table_privilege(current_user,to_regclass(name),'INSERT'),
   'rowSecurityActive',row_security_active(to_regclass(name))) ORDER BY name) FROM relations),
 'columns',(SELECT jsonb_agg(to_jsonb(c) ORDER BY name,attnum) FROM cols c),
 'constraints',(SELECT jsonb_agg(to_jsonb(c) ORDER BY name,conname) FROM cons c),
 'triggers',(SELECT jsonb_agg(to_jsonb(t) ORDER BY name,tgname) FROM trigs t)) AS details)
SELECT md5(details::text),details INTO v_actual_schema,v_schema_details FROM facts;

    IF v_actual_schema IS DISTINCT FROM v_expected_schema_signature THEN
        RAISE EXCEPTION 'STOP: schema/constraint/trigger drift; repeat preflight (actual signature %)',v_actual_schema;
    END IF;
    IF EXISTS (SELECT 1 FROM jsonb_array_elements(v_schema_details->'relations') z
        WHERE (z->>'exists') IS DISTINCT FROM 'true' OR (z->>'selectGranted') IS DISTINCT FROM 'true')
       OR has_table_privilege(current_user,'render.render_jobs','UPDATE') IS DISTINCT FROM true
       OR has_table_privilege(current_user,'video_render.scene_video_versions','UPDATE') IS DISTINCT FROM true
       OR has_table_privilege(current_user,'render.render_job_events','INSERT') IS DISTINCT FROM true
       OR has_table_privilege(current_user,'video_render.video_project_events','INSERT') IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'STOP: operator lacks required table visibility/write/audit privileges';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM video_render.video_projects WHERE id=188 AND tenant_id=c_tenant AND core_job_id=c_core) THEN
        RAISE EXCEPTION 'STOP: tenant/project/core linkage mismatch';
    END IF;
    IF EXISTS (SELECT 1 FROM render.render_job_events WHERE tenant_id=c_tenant AND event_type=c_event
        AND data_json->>'batchId'=v_batch_id::text)
       OR EXISTS (SELECT 1 FROM video_render.video_project_events WHERE tenant_id=c_tenant AND project_id=188
        AND event_type=c_event AND data_json->>'batchId'=v_batch_id::text) THEN
        RAISE EXCEPTION 'STOP: batch audit already exists; use postcheck, do not repeat reconciliation';
    END IF;
    -- Same scene lock key/order as backend. Operator MUST fence old workers; expired leases alone are insufficient.
    FOR op_target IN SELECT generate_series(831,837)::bigint AS scene_id LOOP
        PERFORM pg_advisory_xact_lock(hashtextextended('render.render_jobs:render_scene_video:scene:'||op_target.scene_id::text,0));
    END LOOP;
    PERFORM id FROM video_render.video_project_scenes WHERE tenant_id=c_tenant AND project_id=188 AND id BETWEEN 831 AND 837 ORDER BY id FOR UPDATE;
    IF (SELECT count(*) FROM video_render.video_project_scenes WHERE tenant_id=c_tenant AND project_id=188 AND id BETWEEN 831 AND 837)<>7 THEN
        RAISE EXCEPTION 'STOP: missing/mismatched scene';
    END IF;
    PERFORM id FROM video_render.scene_video_versions WHERE tenant_id=c_tenant AND scene_id BETWEEN 831 AND 837 ORDER BY id FOR UPDATE;
    PERFORM id FROM render.render_jobs WHERE tenant_id=c_tenant AND job_type='render_scene_video'
      AND COALESCE(input_json->>'sceneId',input_json->>'scene_id') IN ('831','832','833','834','835','836','837') ORDER BY id FOR UPDATE;
    PERFORM owner_token FROM render.scene_video_submit_intents WHERE tenant_id=c_tenant AND scene_id BETWEEN 831 AND 837 ORDER BY owner_token FOR UPDATE;
    SELECT md5(to_jsonb(pr)::text) INTO project_hash FROM video_render.video_projects pr WHERE pr.id=188 AND pr.tenant_id=c_tenant;
    SELECT md5(COALESCE(jsonb_agg(to_jsonb(pj) ORDER BY pj.id),'[]'::jsonb)::text) INTO parent_jobs_hash
      FROM render.render_jobs pj WHERE pj.tenant_id=c_tenant
      AND pj.id IN (c_core,'046bcf43-2ecf-4b97-9952-68dfc3f8e9eb'::uuid);
    -- Validate ALL seven before changing any row. Any failure rolls back the entire batch.
    FOR op_target IN SELECT (x->>'sceneId')::bigint AS scene_id,(x->>'jobId')::uuid AS job_id,
        (x->>'versionId')::uuid AS version_id,x AS approval FROM jsonb_array_elements(v_manifest) x ORDER BY (x->>'sceneId')::bigint LOOP
        SELECT * INTO STRICT op_job FROM render.render_jobs WHERE id=op_target.job_id AND tenant_id=c_tenant;
        SELECT * INTO STRICT op_version FROM video_render.scene_video_versions WHERE id=op_target.version_id AND tenant_id=c_tenant;
        IF op_job.job_type IS DISTINCT FROM 'render_scene_video' OR op_job.status IS DISTINCT FROM 'pending_reconciliation'
           OR op_job.error_code IS DISTINCT FROM 'SCENE_VIDEO_MANUAL_REVIEW_REQUIRED' OR op_version.status IS DISTINCT FROM 'queued'
           OR op_version.project_id IS DISTINCT FROM 188 OR op_version.scene_id IS DISTINCT FROM op_target.scene_id OR op_version.render_job_id IS DISTINCT FROM op_job.id
           OR COALESCE(op_job.input_json->>'projectId',op_job.input_json->>'project_id') IS DISTINCT FROM '188'
           OR COALESCE(op_job.input_json->>'sceneId',op_job.input_json->>'scene_id') IS DISTINCT FROM op_target.scene_id::text
           OR op_version.logical_request_id IS DISTINCT FROM op_target.approval->>'logicalRequestId'
           OR op_version.logical_request_id IS DISTINCT FROM COALESCE(op_job.input_json->>'logicalRequestId',op_job.input_json->>'logical_request_id')
           OR op_job.updated_at IS DISTINCT FROM (op_target.approval->>'jobUpdatedAt')::timestamptz
           OR op_version.updated_at IS DISTINCT FROM (op_target.approval->>'versionUpdatedAt')::timestamptz
           OR md5(to_jsonb(op_job)::text) IS DISTINCT FROM op_target.approval->>'jobSnapshotHash'
           OR md5(to_jsonb(op_version)::text) IS DISTINCT FROM op_target.approval->>'versionSnapshotHash' THEN
            RAISE EXCEPTION 'STOP scene %: identity/status/full-row snapshot changed since preflight',op_target.scene_id;
        END IF;
        IF op_job.lock_until>clock_timestamp() OR op_version.is_selected IS DISTINCT FROM false OR op_version.submitted_at IS NOT NULL OR op_version.completed_at IS NOT NULL
           OR op_version.result_media_id IS NOT NULL OR NULLIF(btrim(op_version.public_url),'') IS NOT NULL OR NULLIF(btrim(op_version.source_file_path),'') IS NOT NULL
           OR NULLIF(btrim(op_job.provider_task_id),'') IS NOT NULL OR NULLIF(btrim(op_job.provider_video_id_base),'') IS NOT NULL
           OR NULLIF(btrim(op_version.provider_task_id),'') IS NOT NULL OR NULLIF(btrim(op_version.provider_video_id_base),'') IS NOT NULL
           OR COALESCE(op_job.output_json,'null'::jsonb) NOT IN ('[]'::jsonb,'{}'::jsonb) THEN
            RAISE EXCEPTION 'STOP scene %: live lease, provider/result/submission identity or selected output exists',op_target.scene_id;
        END IF;
        IF EXISTS (SELECT 1 FROM video_render.video_project_scenes sc WHERE sc.id=op_target.scene_id AND sc.tenant_id=c_tenant AND sc.selected_video_version_id IS NOT NULL) THEN
            RAISE EXCEPTION 'STOP scene %: selected video now exists; supplied preflight facts have changed',op_target.scene_id;
        END IF;
        IF EXISTS (SELECT 1 FROM (VALUES (op_job.input_json),(op_version.render_config_json),(op_version.render_config_json->'input')) docs(doc)
            CROSS JOIN LATERAL jsonb_each_text(COALESCE(docs.doc,'{}'::jsonb)) kv
            WHERE lower(replace(kv.key,'_','')) IN ('providertaskid','providervideoidbase')
              AND NULLIF(btrim(kv.value),'') IS NOT NULL) THEN
            RAISE EXCEPTION 'STOP scene %: persisted video input/config retains provider identity',op_target.scene_id;
        END IF;
        IF (SELECT count(*) FROM video_render.scene_video_versions WHERE tenant_id=c_tenant AND render_job_id=op_job.id)<>1
           OR EXISTS (SELECT 1 FROM render.scene_video_submit_intents i WHERE i.tenant_id=c_tenant
                AND (i.render_job_id=op_job.id OR i.logical_request_id=op_version.logical_request_id)) THEN
            RAISE EXCEPTION 'STOP scene %: additional attempt version or ANY target submit intent exists',op_target.scene_id;
        END IF;
        IF NOT EXISTS (SELECT 1 FROM render.render_job_events e WHERE e.tenant_id=c_tenant AND e.job_id=op_job.id
             AND e.event_type='JOB_PENDING_RECONCILIATION'
             AND e.message=c_message AND e.created_at>=op_job.created_at) THEN
            RAISE EXCEPTION 'STOP scene %: exact pre-HTTP identity-guard event is missing',op_target.scene_id;
        END IF;
        IF EXISTS (SELECT 1 FROM render.render_jobs other WHERE other.tenant_id=c_tenant AND other.job_type='render_scene_video'
             AND other.id<>op_job.id AND COALESCE(other.input_json->>'sceneId',other.input_json->>'scene_id')=op_target.scene_id::text
             AND (other.status IN ('queued','preparing','rendering','processing','post_processing','pending_reconciliation')
                  OR (other.status<>'completed' AND (NULLIF(btrim(other.provider_task_id),'') IS NOT NULL
                                                   OR NULLIF(btrim(other.provider_video_id_base),'') IS NOT NULL))))
           OR EXISTS (SELECT 1 FROM video_render.scene_video_versions other WHERE other.tenant_id=c_tenant AND other.scene_id=op_target.scene_id AND other.id<>op_version.id
             AND (other.status NOT IN ('completed','failed','cancelled')
                  OR (other.status<>'completed' AND (NULLIF(btrim(other.provider_task_id),'') IS NOT NULL
                                                   OR NULLIF(btrim(other.provider_video_id_base),'') IS NOT NULL)))) THEN
            RAISE EXCEPTION 'STOP scene %: competing/unresolved job or version; reconcile it separately',op_target.scene_id;
        END IF;
        -- No positive progression beyond this guard is accepted, even if it may itself precede HTTP.
        IF EXISTS (SELECT 1 FROM render.render_job_events e WHERE e.tenant_id=c_tenant AND e.job_id=op_job.id
             AND (e.event_type ~* '(SUBMIT|PROVIDER|POLL)' OR NULLIF(e.data_json->>'providerTaskId','') IS NOT NULL
                  OR NULLIF(e.data_json->>'providerVideoIdBase','') IS NOT NULL))
           OR EXISTS (SELECT 1 FROM video_render.video_project_events e WHERE e.tenant_id=c_tenant AND e.project_id=188
             AND e.created_at>=op_job.created_at AND (e.data_json->>'jobId'=op_job.id::text OR e.data_json->>'renderJobId'=op_job.id::text
                  OR e.data_json->>'sceneVideoVersionId'=op_version.id::text OR e.data_json->>'logicalRequestId'=op_version.logical_request_id
                  OR e.data_json->>'sceneId'=op_target.scene_id::text)
             AND (e.event_type ~* '(SUBMIT|PROVIDER|POLL)' OR NULLIF(e.data_json->>'providerTaskId','') IS NOT NULL
                  OR NULLIF(e.data_json->>'providerVideoIdBase','') IS NOT NULL)) THEN
            RAISE EXCEPTION 'STOP scene %: submit/provider/poll event conflicts with approved no-progression evidence',op_target.scene_id;
        END IF;
        IF EXISTS (SELECT 1 FROM billing.ai_image_billing_records b WHERE b.tenant_id=c_tenant
             AND (replace(b.render_job_id::text,'-','')=replace(op_job.id::text,'-','') OR b.logical_request_id=op_version.logical_request_id)
             AND NULLIF(btrim(b.provider_task_id),'') IS NOT NULL) THEN
            RAISE EXCEPTION 'STOP scene %: billing retains provider identity',op_target.scene_id;
        END IF;
        SELECT md5(to_jsonb(s)::text) INTO scene_hash FROM video_render.video_project_scenes s WHERE s.id=op_target.scene_id AND s.tenant_id=c_tenant;
        SELECT md5(COALESCE(jsonb_agg(to_jsonb(i) ORDER BY i.id),'[]'::jsonb)::text) INTO images_hash
          FROM video_render.scene_image_versions i WHERE i.tenant_id=c_tenant AND i.scene_id=op_target.scene_id;
        SELECT md5(COALESCE(jsonb_agg(to_jsonb(b) ORDER BY to_jsonb(b)::text),'[]'::jsonb)::text) INTO billing_hash
          FROM billing.ai_image_billing_records b WHERE b.tenant_id=c_tenant
          AND (replace(b.render_job_id::text,'-','')=replace(op_job.id::text,'-','') OR b.logical_request_id=op_version.logical_request_id);
        SELECT md5(COALESCE(jsonb_agg(to_jsonb(o) ORDER BY o.id),'[]'::jsonb)::text) INTO other_versions_hash
          FROM video_render.scene_video_versions o WHERE o.tenant_id=c_tenant AND o.scene_id=op_target.scene_id AND o.id<>op_version.id;
        v_before:=v_before||jsonb_build_array(jsonb_build_object('approval',op_target.approval,'sceneId',op_target.scene_id,'jobId',op_job.id,'versionId',op_version.id,
          'logicalRequestId',op_version.logical_request_id,
          'guardEventIds',(SELECT jsonb_agg(e.id ORDER BY e.created_at,e.id) FROM render.render_job_events e WHERE e.tenant_id=c_tenant AND e.job_id=op_job.id AND e.event_type='JOB_PENDING_RECONCILIATION' AND e.message=c_message),'sceneHash',scene_hash,'imagesHash',images_hash,'billingHash',billing_hash,
          'otherVersionsHash',other_versions_hash,
          'jobProtectedHash',md5((to_jsonb(op_job)-ARRAY['status','error_code','error_message','completed_at','updated_at','retry_after','lock_owner','worker_key','lock_until'])::text),
          'versionProtectedHash',md5((to_jsonb(op_version)-ARRAY['status','error_code','error_message','completed_at','updated_at'])::text),
          'jobBefore',jsonb_build_object('status',op_job.status,'errorCode',op_job.error_code,'errorMessage',op_job.error_message,
             'completedAt',op_job.completed_at,'updatedAt',op_job.updated_at,'retryAfter',op_job.retry_after,'lockOwner',op_job.lock_owner,'workerKey',op_job.worker_key,'lockUntil',op_job.lock_until),
          'versionBefore',jsonb_build_object('status',op_version.status,'errorCode',op_version.error_code,'errorMessage',op_version.error_message,'completedAt',op_version.completed_at,'updatedAt',op_version.updated_at)));
    END LOOP;
    FOR op_snapshot IN SELECT value FROM jsonb_array_elements(v_before) LOOP
        UPDATE render.render_jobs SET status='failed',error_code=c_reason,
          error_message='Operator verified pre-HTTP DTO ownership refusal; no provider submission. Historical operation closed.',
          completed_at=COALESCE(completed_at,now()),retry_after=NULL,lock_owner=NULL,worker_key=NULL,lock_until=NULL,updated_at=now()
         WHERE id=(op_snapshot->>'jobId')::uuid AND tenant_id=c_tenant AND job_type='render_scene_video'
           AND status='pending_reconciliation' AND md5(to_jsonb(render_jobs)::text)=op_snapshot->'approval'->>'jobSnapshotHash';
        GET DIAGNOSTICS n=ROW_COUNT; IF n<>1 THEN RAISE EXCEPTION 'Exact job update refused: %',op_snapshot->>'jobId'; END IF;
        UPDATE video_render.scene_video_versions SET status='failed',error_code=c_reason,
          error_message='Operator verified pre-HTTP DTO ownership refusal; no provider submission. Historical version retained.',
          completed_at=COALESCE(completed_at,now()),updated_at=now()
         WHERE id=(op_snapshot->>'versionId')::uuid AND render_job_id=(op_snapshot->>'jobId')::uuid AND tenant_id=c_tenant
           AND scene_id=(op_snapshot->>'sceneId')::bigint AND project_id=188 AND status='queued' AND NOT is_selected
           AND md5(to_jsonb(scene_video_versions)::text)=op_snapshot->'approval'->>'versionSnapshotHash';
        GET DIAGNOSTICS n=ROW_COUNT; IF n<>1 THEN RAISE EXCEPTION 'Exact version update refused: %',op_snapshot->>'versionId'; END IF;
        receipt:=op_snapshot||jsonb_build_object('batchId',v_batch_id,'tenantId',c_tenant,'projectId',188,'coreJobId',c_core,'batchJobId','046bcf43-2ecf-4b97-9952-68dfc3f8e9eb',
            'reason',c_reason,'projectHash',project_hash,'parentJobsHash',parent_jobs_hash,'operator',v_operator,'approvalReference',v_approval_reference,'schemaSignature',v_actual_schema,
            'evidenceKind','CONFIRMED_PRE_HTTP_NO_SUBMIT','reconciledAt',clock_timestamp(),'databaseUser',current_user);
        INSERT INTO render.render_job_events(job_id,tenant_id,event_type,level,message,data_json,created_at)
        VALUES((op_snapshot->>'jobId')::uuid,c_tenant,c_event,'warning','Operator closed an exact historical pre-HTTP no-submit operation.',receipt,now());
        INSERT INTO video_render.video_project_events(project_id,tenant_id,event_type,level,message,data_json,created_at)
        VALUES(188,c_tenant,c_event,'warning','Operator retained historical version and closed its pre-HTTP no-submit operation.',receipt,now());
    END LOOP;
    -- Recheck all protected data and actual backend blockers before COMMIT; any trigger side-effect aborts all seven.
    FOR op_target IN SELECT (x->>'sceneId')::bigint AS scene_id,x AS snapshot FROM jsonb_array_elements(v_before) x LOOP
        op_snapshot:=op_target.snapshot;
        SELECT * INTO STRICT op_job FROM render.render_jobs WHERE id=(op_snapshot->>'jobId')::uuid AND tenant_id=c_tenant;
        SELECT * INTO STRICT op_version FROM video_render.scene_video_versions WHERE id=(op_snapshot->>'versionId')::uuid AND tenant_id=c_tenant;
        IF (SELECT count(*) FROM render.render_job_events je
            JOIN video_render.video_project_events pe ON pe.tenant_id=je.tenant_id AND pe.project_id=188
             AND pe.event_type=c_event AND pe.data_json=je.data_json
            WHERE je.tenant_id=c_tenant AND je.job_id=op_job.id AND je.event_type=c_event
             AND je.data_json->>'batchId'=v_batch_id::text
             AND je.data_json->>'jobId'=op_job.id::text
             AND je.data_json->>'versionId'=op_version.id::text)<>1 THEN
            RAISE EXCEPTION 'STOP scene %: exact job/project audit receipt mismatch; ROLLBACK all seven',op_target.scene_id;
        END IF;
        IF EXISTS (SELECT 1 FROM render.scene_video_submit_intents i WHERE i.tenant_id=c_tenant
            AND (i.render_job_id=op_job.id OR i.logical_request_id=op_version.logical_request_id)) THEN
            RAISE EXCEPTION 'STOP scene %: target submit intent appeared; ROLLBACK all seven',op_target.scene_id;
        END IF;
        IF op_job.status<>'failed' OR op_version.status<>'failed' OR op_job.error_code IS DISTINCT FROM c_reason OR op_version.error_code IS DISTINCT FROM c_reason
           OR op_job.retry_after IS NOT NULL OR op_job.lock_owner IS NOT NULL OR op_job.worker_key IS NOT NULL OR op_job.lock_until IS NOT NULL
           OR md5((to_jsonb(op_job)-ARRAY['status','error_code','error_message','completed_at','updated_at','retry_after','lock_owner','worker_key','lock_until'])::text) IS DISTINCT FROM op_snapshot->>'jobProtectedHash'
           OR md5((to_jsonb(op_version)-ARRAY['status','error_code','error_message','completed_at','updated_at'])::text) IS DISTINCT FROM op_snapshot->>'versionProtectedHash'
           OR (SELECT md5(to_jsonb(s)::text) FROM video_render.video_project_scenes s WHERE s.id=op_target.scene_id AND s.tenant_id=c_tenant) IS DISTINCT FROM op_snapshot->>'sceneHash'
           OR (SELECT md5(COALESCE(jsonb_agg(to_jsonb(i) ORDER BY i.id),'[]'::jsonb)::text) FROM video_render.scene_image_versions i WHERE i.tenant_id=c_tenant AND i.scene_id=op_target.scene_id) IS DISTINCT FROM op_snapshot->>'imagesHash'
           OR (SELECT md5(COALESCE(jsonb_agg(to_jsonb(o) ORDER BY o.id),'[]'::jsonb)::text) FROM video_render.scene_video_versions o WHERE o.tenant_id=c_tenant AND o.scene_id=op_target.scene_id AND o.id<>op_version.id) IS DISTINCT FROM op_snapshot->>'otherVersionsHash'
           OR (SELECT md5(COALESCE(jsonb_agg(to_jsonb(z) ORDER BY to_jsonb(z)::text),'[]'::jsonb)::text) FROM billing.ai_image_billing_records z WHERE z.tenant_id=c_tenant AND (replace(z.render_job_id::text,'-','')=replace(op_job.id::text,'-','') OR z.logical_request_id=op_version.logical_request_id)) IS DISTINCT FROM op_snapshot->>'billingHash' THEN
            RAISE EXCEPTION 'STOP scene %: unexpected selection/image/billing/history/protected-field change; ROLLBACK all seven',op_target.scene_id;
        END IF;
        SELECT CASE
            WHEN EXISTS (SELECT 1 FROM render.scene_video_submit_intents i
                JOIN render.render_jobs prior ON prior.id=i.render_job_id AND prior.tenant_id=i.tenant_id
                WHERE i.tenant_id=c_tenant AND i.scene_id=op_target.scene_id
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
                WHERE j.tenant_id=c_tenant AND j.job_type='render_scene_video'
                  AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id')=op_target.scene_id::text
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
                WHERE v.tenant_id=c_tenant AND v.scene_id=op_target.scene_id
                  AND ((v.status NOT IN ('completed', 'failed') AND
                        (NULLIF(btrim(v.provider_task_id), '') IS NOT NULL
                         OR NULLIF(btrim(v.provider_video_id_base), '') IS NOT NULL))
                    OR v.status='pending_reconciliation'))
                THEN 3
            ELSE 0
        END INTO blocker;
        IF blocker<>0 THEN RAISE EXCEPTION 'STOP scene %: backend blocker % remains; ROLLBACK all seven',op_target.scene_id,blocker; END IF;
        IF EXISTS (SELECT 1 FROM render.render_jobs q WHERE q.tenant_id=c_tenant AND q.job_type='render_scene_video'
            AND COALESCE(q.input_json->>'sceneId',q.input_json->>'scene_id')=op_target.scene_id::text
            AND q.status IN ('queued','preparing','rendering','processing','post_processing','pending_reconciliation')) THEN
            RAISE EXCEPTION 'STOP scene %: active job remains; ROLLBACK all seven',op_target.scene_id;
        END IF;
    END LOOP;
    IF (SELECT md5(to_jsonb(pr)::text) FROM video_render.video_projects pr WHERE pr.id=188 AND pr.tenant_id=c_tenant) IS DISTINCT FROM project_hash
       OR (SELECT md5(COALESCE(jsonb_agg(to_jsonb(pj) ORDER BY pj.id),'[]'::jsonb)::text) FROM render.render_jobs pj
           WHERE pj.tenant_id=c_tenant AND pj.id IN (c_core,'046bcf43-2ecf-4b97-9952-68dfc3f8e9eb'::uuid)) IS DISTINCT FROM parent_jobs_hash THEN
        RAISE EXCEPTION 'STOP: project/core/batch parent changed unexpectedly; ROLLBACK all seven';
    END IF;
    IF (SELECT count(*) FROM render.render_job_events WHERE tenant_id=c_tenant AND event_type=c_event AND data_json->>'batchId'=v_batch_id::text)<>7
       OR (SELECT count(*) FROM video_render.video_project_events WHERE tenant_id=c_tenant AND project_id=188 AND event_type=c_event AND data_json->>'batchId'=v_batch_id::text)<>7 THEN
        RAISE EXCEPTION 'STOP: audit count mismatch; ROLLBACK all seven';
    END IF;
    RAISE NOTICE 'Seven exact historical operations closed; no new job/render created. Run read-only postcheck for batch %.',v_batch_id;
END;
$project188$;
COMMIT;
