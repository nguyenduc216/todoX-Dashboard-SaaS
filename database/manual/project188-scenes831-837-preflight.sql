-- SUPPLEMENTAL PREFLIGHT ONLY: uses the seven UUID pairs already supplied and reviewed.
-- Does NOT repeat the previous full job/version/intent/event inventory.
-- READ ONLY. Operator executes complete script; Codex does not connect to production.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL TIME ZONE 'UTC';
SET LOCAL row_security=off; -- fail if policy filtering would hide evidence; no bypass is granted
SELECT current_database() AS database_name,current_user AS operator_role,clock_timestamp() AS captured_at,
 current_setting('server_version') AS server_version;
-- Missing production schema/constraints/trigger/grant facts. Save signature AND details.
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
SELECT md5(details::text) AS schema_signature,details FROM facts;
-- Audit sequence privileges, if an audit ID uses a serial/identity sequence.
WITH audit_tables(name) AS (VALUES ('render.render_job_events'),('video_render.video_project_events'))
SELECT name,pg_get_serial_sequence(name,'id') AS id_sequence,
 CASE WHEN pg_get_serial_sequence(name,'id') IS NULL THEN NULL
      ELSE has_sequence_privilege(current_user,pg_get_serial_sequence(name,'id'),'USAGE') END AS sequence_usage
FROM audit_tables;
-- Only fresh microsecond values, row fingerprints and lease/drift facts not established by the old output.
-- The supplied millisecond timestamps are NOT used as precise concurrency predicates.
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
]'::jsonb)
 AS t("sceneId" bigint,"jobId" uuid,"versionId" uuid,"logicalRequestId" text)),
observed AS (
 SELECT f.*,j.updated_at AS job_updated_at,v.updated_at AS version_updated_at,
 to_char(j.updated_at AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI:SS.US"Z"') AS job_updated_at_utc_microseconds,
 to_char(v.updated_at AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI:SS.US"Z"') AS version_updated_at_utc_microseconds,
 j.lock_owner,j.worker_key,j.lock_until,j.retry_after,
 (j.lock_until IS NULL OR j.lock_until<=clock_timestamp()) AS lease_expired_now,
 md5(to_jsonb(j)::text) AS job_snapshot_hash,md5(to_jsonb(v)::text) AS version_snapshot_hash,
 (j.id IS NOT NULL AND v.id IS NOT NULL AND v.render_job_id=j.id AND v.scene_id=f."sceneId" AND v.project_id=188
   AND v.logical_request_id=f."logicalRequestId" AND j.job_type='render_scene_video'
   AND COALESCE(j.input_json->>'projectId',j.input_json->>'project_id')='188'
   AND COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id')=f."sceneId"::text
   AND COALESCE(j.input_json->>'logicalRequestId',j.input_json->>'logical_request_id')=f."logicalRequestId"
   AND j.status='pending_reconciliation' AND j.error_code='SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
   AND v.status='queued') AS matches_supplied_identity_and_state
 FROM fixed f LEFT JOIN render.render_jobs j ON j.id=f."jobId" AND j.tenant_id='95f5db58-4233-4dfe-9747-5276105af12a'::uuid
 LEFT JOIN video_render.scene_video_versions v ON v.id=f."versionId" AND v.tenant_id='95f5db58-4233-4dfe-9747-5276105af12a'::uuid
)
SELECT jsonb_agg(jsonb_build_object('sceneId',"sceneId",'jobId',"jobId",'versionId',"versionId",'logicalRequestId',"logicalRequestId",
 'jobUpdatedAt',job_updated_at_utc_microseconds,'versionUpdatedAt',version_updated_at_utc_microseconds,
 'jobSnapshotHash',job_snapshot_hash,'versionSnapshotHash',version_snapshot_hash,
 'approved',false,'workerFenced',false,'httpOutcome','UNREVIEWED','noSubmitEvidenceReference',NULL,
 'projectEventsReviewed',false,'projectEventsReference',NULL,'applicationLogsStatus','UNREVIEWED','applicationLogsReference',NULL)
 ORDER BY "sceneId") AS supplemental_manifest,
 jsonb_agg(jsonb_build_object('sceneId',"sceneId",'matchesSuppliedFacts',matches_supplied_identity_and_state,
 'jobUpdatedAtMicroseconds',job_updated_at_utc_microseconds,'versionUpdatedAtMicroseconds',version_updated_at_utc_microseconds,
 'leaseExpiredNow',lease_expired_now,'lockOwner',lock_owner,'workerKey',worker_key,'lockUntil',lock_until,'retryAfter',retry_after)
 ORDER BY "sceneId") AS current_drift_and_lease_facts
FROM observed;
-- Lease observations do NOT prove a worker is fenced. Independent operator confirmation remains mandatory.
-- No query can manufacture missing independent outbound-HTTP/provider evidence.
COMMIT;
