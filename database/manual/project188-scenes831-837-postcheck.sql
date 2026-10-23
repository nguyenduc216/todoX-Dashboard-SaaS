-- EXACT TARGETS / READ ONLY. Replace ONLY NULL::uuid below with the APPROVED reconciliation batch UUID.
-- Run immediately after the reviewed reconciliation, before starting a new operation.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL TIME ZONE 'UTC';
SET LOCAL row_security=off; -- fail rather than silently omit rows under RLS; does not grant a bypass
WITH p AS (SELECT '95f5db58-4233-4dfe-9747-5276105af12a'::uuid AS tenant_id,NULL::uuid AS batch_id),
fixed AS (SELECT * FROM jsonb_to_recordset('[
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
]'::jsonb) AS t("sceneId" bigint,"jobId" uuid,"versionId" uuid,"logicalRequestId" text)),
audit AS (
 SELECT e.job_id,e.data_json AS receipt FROM p JOIN render.render_job_events e ON e.tenant_id=p.tenant_id
 WHERE p.batch_id IS NOT NULL AND e.event_type='PROJECT188_PREHTTP_NO_SUBMIT_RECONCILED'
  AND e.data_json->>'batchId'=p.batch_id::text AND e.data_json->>'projectId'='188'
), checks AS (
 SELECT a.job_id,(a.receipt->>'sceneId')::bigint AS scene_id,(a.receipt->>'versionId')::uuid AS version_id,
 j.status='failed' AND v.status='failed' AND j.error_code='RVIDEO_PREHTTP_DTO_OWNER_RECONCILED'
   AND v.error_code='RVIDEO_PREHTTP_DTO_OWNER_RECONCILED' AND j.completed_at IS NOT NULL AND v.completed_at IS NOT NULL AS status_closed,
 j.lock_until IS NULL AND j.lock_owner IS NULL AND j.worker_key IS NULL AND j.retry_after IS NULL AS no_lease_retry,
 v.render_job_id=j.id AND v.scene_id=(a.receipt->>'sceneId')::bigint AND v.project_id=188
   AND v.logical_request_id=a.receipt->>'logicalRequestId' AND NOT v.is_selected
   AND NULLIF(btrim(j.provider_task_id),'') IS NULL AND NULLIF(btrim(j.provider_video_id_base),'') IS NULL
   AND NULLIF(btrim(v.provider_task_id),'') IS NULL AND NULLIF(btrim(v.provider_video_id_base),'') IS NULL
   AND v.submitted_at IS NULL AND v.result_media_id IS NULL AS exact_no_task_identity,
 (SELECT md5(to_jsonb(pr)::text) FROM video_render.video_projects pr WHERE pr.id=188 AND pr.tenant_id=p.tenant_id)=a.receipt->>'projectHash' AS project_unchanged,
 (SELECT md5(COALESCE(jsonb_agg(to_jsonb(pj) ORDER BY pj.id),'[]'::jsonb)::text) FROM render.render_jobs pj WHERE pj.tenant_id=p.tenant_id
   AND pj.id IN ('2f3664eb-cdf6-44bd-aa6f-8ef12cc8b432'::uuid,'046bcf43-2ecf-4b97-9952-68dfc3f8e9eb'::uuid))=a.receipt->>'parentJobsHash' AS parent_jobs_unchanged,
 md5((to_jsonb(j)-ARRAY['status','error_code','error_message','completed_at','updated_at','retry_after','lock_owner','worker_key','lock_until'])::text)=a.receipt->>'jobProtectedHash' AS job_protected,
 md5((to_jsonb(v)-ARRAY['status','error_code','error_message','completed_at','updated_at'])::text)=a.receipt->>'versionProtectedHash' AS version_protected,
 (SELECT md5(to_jsonb(s)::text) FROM video_render.video_project_scenes s WHERE s.tenant_id=p.tenant_id AND s.id=v.scene_id)=a.receipt->>'sceneHash' AS scene_unchanged,
 (SELECT md5(COALESCE(jsonb_agg(to_jsonb(i) ORDER BY i.id),'[]'::jsonb)::text) FROM video_render.scene_image_versions i WHERE i.tenant_id=p.tenant_id AND i.scene_id=v.scene_id)=a.receipt->>'imagesHash' AS images_unchanged,
 (SELECT md5(COALESCE(jsonb_agg(to_jsonb(o) ORDER BY o.id),'[]'::jsonb)::text) FROM video_render.scene_video_versions o WHERE o.tenant_id=p.tenant_id AND o.scene_id=v.scene_id AND o.id<>v.id)=a.receipt->>'otherVersionsHash' AS other_versions_unchanged,
 (SELECT md5(COALESCE(jsonb_agg(to_jsonb(z) ORDER BY to_jsonb(z)::text),'[]'::jsonb)::text) FROM billing.ai_image_billing_records z WHERE z.tenant_id=p.tenant_id AND (replace(z.render_job_id::text,'-','')=replace(j.id::text,'-','') OR z.logical_request_id=v.logical_request_id))=a.receipt->>'billingHash' AS billing_unchanged,
 EXISTS (SELECT 1 FROM video_render.video_project_events e WHERE e.tenant_id=p.tenant_id AND e.project_id=188
   AND e.event_type='PROJECT188_PREHTTP_NO_SUBMIT_RECONCILED' AND e.data_json=a.receipt) AS matching_project_audit,
 NOT EXISTS (SELECT 1 FROM render.scene_video_submit_intents i WHERE i.tenant_id=p.tenant_id AND (i.render_job_id=j.id OR i.logical_request_id=v.logical_request_id)) AS no_target_intent
 FROM p JOIN audit a ON true JOIN fixed f ON f."jobId"=a.job_id
 AND f."versionId"=(a.receipt->>'versionId')::uuid AND f."sceneId"=(a.receipt->>'sceneId')::bigint
 AND f."logicalRequestId"=a.receipt->>'logicalRequestId' JOIN render.render_jobs j ON j.id=a.job_id AND j.tenant_id=p.tenant_id
 JOIN video_render.scene_video_versions v ON v.id=(a.receipt->>'versionId')::uuid AND v.tenant_id=p.tenant_id
), blockers AS (
 SELECT g.scene_id,b.block_reason,
 (SELECT count(*) FROM render.render_jobs j WHERE j.tenant_id=p.tenant_id AND j.job_type='render_scene_video'
  AND COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id')=g.scene_id::text
  AND j.status IN ('queued','preparing','rendering','processing','post_processing','pending_reconciliation')) AS active_jobs
 FROM p CROSS JOIN generate_series(831,837) g(scene_id) CROSS JOIN LATERAL (
SELECT CASE
    WHEN EXISTS (SELECT 1 FROM render.scene_video_submit_intents i
        JOIN render.render_jobs prior ON prior.id=i.render_job_id AND prior.tenant_id=i.tenant_id
        WHERE i.tenant_id=p.tenant_id AND i.scene_id=g.scene_id
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
          AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id')=g.scene_id::text
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
        WHERE v.tenant_id=p.tenant_id AND v.scene_id=g.scene_id
          AND ((v.status NOT IN ('completed', 'failed') AND
                (NULLIF(btrim(v.provider_task_id), '') IS NOT NULL
                 OR NULLIF(btrim(v.provider_video_id_base), '') IS NOT NULL))
            OR v.status='pending_reconciliation'))
        THEN 3
    ELSE 0
END AS block_reason) b
), summary AS (
 SELECT p.batch_id IS NOT NULL AS batch_id_supplied,(SELECT count(*) FROM audit) AS job_audit_rows,
 (SELECT count(*) FROM checks) AS checked_pairs,
 (SELECT array_agg(scene_id ORDER BY scene_id) FROM checks)=ARRAY[831,832,833,834,835,836,837]::bigint[] AS exact_scene_set,
 (SELECT count(DISTINCT job_id)=7 AND count(DISTINCT version_id)=7 FROM checks) AS exact_unique_pairs,
 COALESCE((SELECT bool_and(status_closed AND no_lease_retry AND exact_no_task_identity AND job_protected
   AND project_unchanged AND parent_jobs_unchanged AND version_protected AND scene_unchanged AND images_unchanged AND other_versions_unchanged
   AND billing_unchanged AND matching_project_audit AND no_target_intent) FROM checks),false) AS all_row_checks_pass,
 (SELECT bool_and(block_reason=0 AND active_jobs=0) FROM blockers) AS all_scenes_clear,
 (SELECT count(*) FROM video_render.video_project_events e WHERE e.tenant_id=p.tenant_id AND e.project_id=188
   AND e.event_type='PROJECT188_PREHTTP_NO_SUBMIT_RECONCILED' AND e.data_json->>'batchId'=p.batch_id::text) AS project_audit_rows
 FROM p
)
SELECT '01_batch' AS section,jsonb_build_object('database',current_database(),'batchId',p.batch_id,'checkedAt',clock_timestamp()) AS evidence FROM p
UNION ALL SELECT '02_pair',to_jsonb(c) FROM checks c
UNION ALL SELECT '03_backend_blocker',to_jsonb(b) FROM blockers b
UNION ALL SELECT '04_summary',to_jsonb(s)||jsonb_build_object('PASS',
 COALESCE(batch_id_supplied AND job_audit_rows=7 AND project_audit_rows=7 AND checked_pairs=7
 AND exact_scene_set AND exact_unique_pairs AND all_row_checks_pass AND all_scenes_clear,false)) FROM summary s
ORDER BY section;
COMMIT;
