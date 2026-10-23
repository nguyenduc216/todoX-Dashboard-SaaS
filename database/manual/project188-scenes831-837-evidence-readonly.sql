-- OPTIONAL MISSING-EVIDENCE EXTRACT ONLY. Use existing saved event exports first.
-- No old full preflight, schema inventory or manifest recollection.
-- Operator executes only if these raw event/lease facts are genuinely unavailable.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL TIME ZONE 'UTC';
SET LOCAL row_security=off;
-- 1. Exact job events; raw prompts/errors/provider payloads intentionally not exported.
WITH fixed AS (SELECT * FROM jsonb_to_recordset('[
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
]'::jsonb) AS t("sceneId" bigint,"jobId" uuid,"versionId" uuid,"logicalRequestId" text))
SELECT f."sceneId",e.job_id,e.id AS event_id,e.event_type,e.level,e.created_at,
 e.message='Selected version does not belong to this job/scene/logical request; submit is prohibited.'
 OR e.data_json->>'errorMessage'='Selected version does not belong to this job/scene/logical request; submit is prohibited.' AS exact_identity_guard_evidence,
 e.event_type ~* '(SUBMIT|PROVIDER|POLL)' AS progression_requires_review,
 COALESCE(NULLIF(e.data_json->>'providerTaskId',''),NULLIF(e.data_json->>'providerVideoIdBase','')) IS NOT NULL AS provider_identity_present
FROM fixed f JOIN render.render_job_events e ON e.job_id=f."jobId"
 AND e.tenant_id='95f5db58-4233-4dfe-9747-5276105af12a'::uuid
ORDER BY f."sceneId",e.created_at,e.id;
-- 2. Correlated project events. A scene/time-only match is not ownership proof.
WITH fixed AS (SELECT * FROM jsonb_to_recordset('[
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
]'::jsonb) AS t("sceneId" bigint,"jobId" uuid,"versionId" uuid,"logicalRequestId" text))
SELECT f."sceneId",f."jobId",f."versionId",e.id AS project_event_id,e.event_type,e.level,e.created_at,
 CASE WHEN replace(f."jobId"::text,'-','')=ANY(ARRAY[replace(lower(e.data_json->>'jobId'),'-',''),replace(lower(e.data_json->>'renderJobId'),'-',''),replace(lower(e.data_json->>'job_id'),'-',''),replace(lower(e.data_json->>'render_job_id'),'-','')]) THEN 'EXACT_JOB'
      WHEN replace(f."versionId"::text,'-','')=ANY(ARRAY[replace(lower(e.data_json->>'sceneVideoVersionId'),'-',''),replace(lower(e.data_json->>'scene_video_version_id'),'-','')]) THEN 'EXACT_VERSION'
      WHEN f."logicalRequestId"=ANY(ARRAY[e.data_json->>'logicalRequestId',e.data_json->>'logical_request_id']) THEN 'EXACT_LOGICAL_REQUEST'
      ELSE 'SCENE_TIME_ONLY_REQUIRES_CORRELATION' END AS correlation_strength,
 e.event_type ~* '(SUBMIT|PROVIDER|POLL)' AS progression_requires_review,
 COALESCE(NULLIF(e.data_json->>'providerTaskId',''),NULLIF(e.data_json->>'providerVideoIdBase','')) IS NOT NULL AS provider_identity_present,
 e.data_json->>'httpStatus' AS http_status,e.data_json->>'countTasks' AS count_tasks
FROM fixed f JOIN render.render_jobs j ON j.id=f."jobId" AND j.tenant_id='95f5db58-4233-4dfe-9747-5276105af12a'::uuid
JOIN video_render.video_project_events e ON e.tenant_id=j.tenant_id AND e.project_id=188
 AND (replace(f."jobId"::text,'-','')=ANY(ARRAY[replace(lower(e.data_json->>'jobId'),'-',''),replace(lower(e.data_json->>'renderJobId'),'-',''),replace(lower(e.data_json->>'job_id'),'-',''),replace(lower(e.data_json->>'render_job_id'),'-','')])
 OR replace(f."versionId"::text,'-','')=ANY(ARRAY[replace(lower(e.data_json->>'sceneVideoVersionId'),'-',''),replace(lower(e.data_json->>'scene_video_version_id'),'-','')])
 OR f."logicalRequestId"=ANY(ARRAY[e.data_json->>'logicalRequestId',e.data_json->>'logical_request_id'])
 OR (COALESCE(e.data_json->>'sceneId',e.data_json->>'scene_id')=f."sceneId"::text AND e.created_at>=j.created_at))
ORDER BY f."sceneId",e.created_at,e.id;
-- 3. Optional CURRENT lease observation for fencing, not a fencing attestation or old preflight repeat.
WITH fixed AS (SELECT * FROM jsonb_to_recordset('[
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
]'::jsonb) AS t("sceneId" bigint,"jobId" uuid,"versionId" uuid,"logicalRequestId" text))
SELECT f."sceneId",j.id AS job_id,j.status,j.lock_owner,j.worker_key,j.lock_until,j.retry_after,
 clock_timestamp() AS observed_at,
 j.lock_until IS NULL OR j.lock_until<=clock_timestamp() AS lease_expired_observation_only,
 false AS worker_fencing_proven_by_this_query
FROM fixed f JOIN render.render_jobs j ON j.id=f."jobId" AND j.tenant_id='95f5db58-4233-4dfe-9747-5276105af12a'::uuid
ORDER BY f."sceneId";
COMMIT;
