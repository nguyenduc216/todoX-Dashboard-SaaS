-- RDance three-job production forensic export.
-- Target database: todo-saas PostgreSQL.
-- Read-only: no provider calls and no database writes.

BEGIN TRANSACTION READ ONLY;

-- ================================================================
-- Confirmed source/schema objects
-- ================================================================
-- dance_sell.dance_sell_jobs
-- dance_sell.dance_sell_provider_operations
-- public.todox_ai_operation_assets
-- render.render_jobs
-- render.render_job_events
-- media.media_files
--
-- Relevant confirmed columns used by joins:
-- dance_sell_jobs: id, render_job_id, reference_render_job_id,
--   motion_render_job_id, character_media_id, product_media_id,
--   direct_reference_media_id, motion_media_id, motion_video_media_id,
--   prepared_reference_media_id, logical_request_id
-- provider_operations: id, dance_sell_job_id, render_job_id
-- render_jobs: id, business_entity_id
-- render_job_events: job_id, render_job_id
-- operation_assets: operation_id, media_id

-- ================================================================
-- Target identifiers
-- ================================================================
WITH targets(dance_sell_job_id, render_job_id, provider_task_id, label) AS (
    VALUES
        ('23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2', 'FAILED_NEW'),
        ('28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662', 'FAILED_PREVIOUS'),
        ('49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8', 'SUCCESS')
)
SELECT * FROM targets ORDER BY label;

-- ================================================================
-- Schema verification: relevant columns first
-- ================================================================
SELECT table_schema, table_name, column_name, data_type, udt_name, ordinal_position
FROM information_schema.columns
WHERE (table_schema, table_name) IN (
    ('dance_sell', 'dance_sell_jobs'),
    ('dance_sell', 'dance_sell_provider_operations'),
    ('public', 'todox_ai_operation_assets'),
    ('render', 'render_jobs'),
    ('render', 'render_job_events'),
    ('media', 'media_files')
)
ORDER BY table_schema, table_name, ordinal_position;

-- ================================================================
-- A. Complete DanceSellJob rows
-- ================================================================
SELECT row_to_json(j) AS dance_sell_job
FROM dance_sell.dance_sell_jobs j
WHERE j.id IN (
    '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid,
    '28171031-d86d-4c67-8eff-67aab154df5f'::uuid,
    '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid
)
ORDER BY j.created_at, j.id;

-- A2. Diagnostic JSON fields as separate pretty JSON values.
SELECT
    j.id AS dance_sell_job_id,
    jsonb_pretty(j.request_json) AS request_json,
    jsonb_pretty(j.submit_response_json) AS submit_response_json,
    jsonb_pretty(j.poll_response_json) AS poll_response_json,
    jsonb_pretty(j.error_json) AS error_json
FROM dance_sell.dance_sell_jobs j
WHERE j.id IN (
    '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid,
    '28171031-d86d-4c67-8eff-67aab154df5f'::uuid,
    '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid
)
ORDER BY j.created_at, j.id;

-- ================================================================
-- B. Complete target RenderJob rows and safe related attempts
-- ================================================================
WITH target_dance_jobs AS (
    SELECT *
    FROM dance_sell.dance_sell_jobs
    WHERE id IN (
        '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid,
        '28171031-d86d-4c67-8eff-67aab154df5f'::uuid,
        '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid
    )
), related_render_ids AS (
    SELECT render_job_id AS id FROM target_dance_jobs WHERE render_job_id IS NOT NULL
    UNION
    SELECT reference_render_job_id FROM target_dance_jobs WHERE reference_render_job_id IS NOT NULL
    UNION
    SELECT motion_render_job_id FROM target_dance_jobs WHERE motion_render_job_id IS NOT NULL
    UNION
    SELECT r.id
    FROM render.render_jobs r
    WHERE r.business_entity_id IN (SELECT id FROM target_dance_jobs)
)
SELECT row_to_json(r) AS render_job
FROM render.render_jobs r
JOIN related_render_ids ids ON ids.id = r.id
ORDER BY r.created_at, r.id;

-- ================================================================
-- C. All provider operations for target jobs/render jobs/tasks
-- ================================================================
WITH target_dance_jobs AS (
    SELECT id
    FROM dance_sell.dance_sell_jobs
    WHERE id IN (
        '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid,
        '28171031-d86d-4c67-8eff-67aab154df5f'::uuid,
        '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid
    )
), target_render_jobs AS (
    SELECT render_job_id AS id FROM dance_sell.dance_sell_jobs WHERE id IN (SELECT id FROM target_dance_jobs) AND render_job_id IS NOT NULL
    UNION SELECT reference_render_job_id FROM dance_sell.dance_sell_jobs WHERE id IN (SELECT id FROM target_dance_jobs) AND reference_render_job_id IS NOT NULL
    UNION SELECT motion_render_job_id FROM dance_sell.dance_sell_jobs WHERE id IN (SELECT id FROM target_dance_jobs) AND motion_render_job_id IS NOT NULL
    UNION SELECT r.id FROM render.render_jobs r WHERE r.business_entity_id IN (SELECT id FROM target_dance_jobs)
), target_tasks(task_id) AS (
    VALUES ('58660402320274b2'), ('56a3fb0578396662'), ('842d3440df5430b8')
)
SELECT row_to_json(o) AS provider_operation
FROM dance_sell.dance_sell_provider_operations o
WHERE o.dance_sell_job_id IN (SELECT id FROM target_dance_jobs)
   OR o.render_job_id IN (SELECT id FROM target_render_jobs)
   OR o.provider_task_id IN (SELECT task_id FROM target_tasks)
ORDER BY o.created_at, o.attempt_no, o.id;

-- C2. Operation response/error JSON explicitly surfaced.
WITH target_dance_jobs AS (
    SELECT id FROM dance_sell.dance_sell_jobs
    WHERE id IN ('23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, '28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid)
)
SELECT id AS operation_id, dance_sell_job_id, render_job_id, attempt_no,
       provider_task_id, provider_code, provider_model, provider_account_id,
       status, provider_status, created_at, started_at, submitted_at, completed_at, failed_at,
       jsonb_pretty(request_json) AS request_json,
       jsonb_pretty(response_json) AS response_json,
       jsonb_pretty(error_json) AS error_json
FROM dance_sell.dance_sell_provider_operations
WHERE dance_sell_job_id IN (SELECT id FROM target_dance_jobs)
ORDER BY created_at, attempt_no, id;

-- ================================================================
-- D. Complete operation assets, including all metadata
-- ================================================================
WITH target_dance_jobs AS (
    SELECT id FROM dance_sell.dance_sell_jobs
    WHERE id IN ('23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, '28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid)
), target_operations AS (
    SELECT id FROM dance_sell.dance_sell_provider_operations
    WHERE dance_sell_job_id IN (SELECT id FROM target_dance_jobs)
)
SELECT row_to_json(a) AS operation_asset
FROM public.todox_ai_operation_assets a
WHERE a.operation_id IN (SELECT id FROM target_operations)
ORDER BY a.created_at, a.operation_id, a.asset_role;

-- D2. Reuse/upload identity fields extracted from metadata_json.
WITH target_dance_jobs AS (
    SELECT id FROM dance_sell.dance_sell_jobs
    WHERE id IN ('23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, '28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid)
), target_operations AS (
    SELECT id FROM dance_sell.dance_sell_provider_operations
    WHERE dance_sell_job_id IN (SELECT id FROM target_dance_jobs)
)
SELECT a.operation_id, a.asset_role, a.media_id, a.provider_url, a.created_at,
       a.metadata_json->>'idBase' AS id_base,
       a.metadata_json->>'uploadUrl' AS upload_url,
       a.metadata_json->>'verificationMatched' AS verification_matched,
       a.metadata_json->>'verificationSource' AS verification_source,
       a.metadata_json->>'verifiedUrl' AS verified_url,
       a.metadata_json->>'providerStatus' AS provider_status,
       a.metadata_json->>'uploadState' AS upload_state,
       a.metadata_json->>'reusedForRenderJobId' AS reused_for_render_job_id,
       a.metadata_json->>'reusedForOperationId' AS reused_for_operation_id,
       jsonb_pretty(a.metadata_json) AS metadata_json
FROM public.todox_ai_operation_assets a
WHERE a.operation_id IN (SELECT id FROM target_operations)
ORDER BY a.created_at, a.operation_id, a.asset_role;

-- ================================================================
-- E. All events for target render jobs
-- ================================================================
WITH target_dance_jobs AS (
    SELECT * FROM dance_sell.dance_sell_jobs
    WHERE id IN ('23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, '28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid)
), target_render_jobs AS (
    SELECT render_job_id AS id FROM target_dance_jobs WHERE render_job_id IS NOT NULL
    UNION SELECT reference_render_job_id FROM target_dance_jobs WHERE reference_render_job_id IS NOT NULL
    UNION SELECT motion_render_job_id FROM target_dance_jobs WHERE motion_render_job_id IS NOT NULL
    UNION SELECT r.id FROM render.render_jobs r WHERE r.business_entity_id IN (SELECT id FROM target_dance_jobs)
)
SELECT row_to_json(e) AS render_event
FROM render.render_job_events e
WHERE e.job_id IN (SELECT id FROM target_render_jobs)
   OR e.render_job_id IN (SELECT id FROM target_render_jobs)
ORDER BY e.created_at, e.id;

-- ================================================================
-- F. All media rows referenced by the three DanceSell jobs
-- ================================================================
WITH target_dance_jobs AS (
    SELECT * FROM dance_sell.dance_sell_jobs
    WHERE id IN ('23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, '28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid)
), media_ids AS (
    SELECT character_media_id AS id FROM target_dance_jobs WHERE character_media_id IS NOT NULL
    UNION SELECT product_media_id FROM target_dance_jobs WHERE product_media_id IS NOT NULL
    UNION SELECT direct_reference_media_id FROM target_dance_jobs WHERE direct_reference_media_id IS NOT NULL
    UNION SELECT motion_media_id FROM target_dance_jobs WHERE motion_media_id IS NOT NULL
    UNION SELECT motion_video_media_id FROM target_dance_jobs WHERE motion_video_media_id IS NOT NULL
    UNION SELECT prepared_reference_media_id FROM target_dance_jobs WHERE prepared_reference_media_id IS NOT NULL
)
SELECT row_to_json(m) AS media_file
FROM media.media_files m
JOIN media_ids ids ON ids.id = m.id
ORDER BY m.created_at, m.id;

-- ================================================================
-- G. Non-secret provider/config evidence
-- ================================================================
-- Historical operations expose provider code/model/account IDs. The request,
-- submit response and event JSON expose non-secret domain/project/model/mode/
-- ratio/subType/background fields when they were persisted. Credentials and
-- authorization values are intentionally not selected.
WITH target_dance_jobs AS (
    SELECT id FROM dance_sell.dance_sell_jobs
    WHERE id IN ('23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, '28171031-d86d-4c67-8eff-67aab154df5f::uuid, '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid)
)
SELECT o.dance_sell_job_id, o.render_job_id, o.provider_code, o.provider_model,
       o.provider_account_id, o.provider_task_id, o.created_at,
       o.request_json - 'access_token' - 'api_key' - 'secret' - 'password' AS non_secret_request_json,
       o.response_json - 'access_token' - 'api_key' - 'secret' - 'password' AS non_secret_response_json,
       o.error_json - 'access_token' - 'api_key' - 'secret' - 'password' AS non_secret_error_json
FROM dance_sell.dance_sell_provider_operations o
WHERE o.dance_sell_job_id IN (SELECT id FROM target_dance_jobs)
ORDER BY o.created_at, o.attempt_no, o.id;

-- Route/provider configuration is resolved at runtime by source code and is
-- not historically copied into every row. This export therefore does not
-- invent a configuration-table join. Use persisted request/event JSON above
-- for domain/project/model/subType/background_source/mode/ratio values.

ROLLBACK;
