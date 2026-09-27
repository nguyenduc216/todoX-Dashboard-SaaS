-- RDance live forensic export for one DanceSell job.
-- READ ONLY: run against the todo-saas PostgreSQL database.
-- No provider calls, retries, uploads, submits, or data modifications.

BEGIN TRANSACTION READ ONLY;

-- Input used by every query below.
SELECT '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid AS dance_sell_job_id;

-- ================================================================
-- Schema inspection
-- ================================================================
SELECT table_schema, table_name, column_name, data_type, udt_name, ordinal_position
FROM information_schema.columns
WHERE (table_schema, table_name) IN (
    ('dance_sell', 'dance_sell_jobs'),
    ('dance_sell', 'dance_sell_provider_operations'),
    ('render', 'render_jobs'),
    ('render', 'render_job_events'),
    ('public', 'todox_ai_operation_assets'),
    ('media', 'media_files')
)
ORDER BY table_schema, table_name, ordinal_position;

-- ================================================================
-- A. DanceSellJob
-- ================================================================
SELECT
    j.id,
    j.tenant_id,
    j.customer_id,
    j.user_id,
    j.title,
    j.reference_mode,
    j.character_media_id,
    j.product_media_id,
    j.direct_reference_media_id,
    j.motion_media_id,
    j.selected_reference_version_id,
    j.reference_render_job_id,
    j.motion_render_job_id,
    j.render_job_id,
    j.motion_video_media_id,
    j.motion_video_object_key,
    j.prepared_reference_media_id,
    j.prepared_reference_object_key,
    j.prepared_reference_url,
    j.prepared_reference_status,
    j.status,
    j.current_stage,
    j.provider_code,
    j.provider_model,
    j.provider_status,
    j.provider_task_id,
    j.request_json::text AS request_json,
    j.submit_response_json::text AS submit_response_json,
    j.poll_response_json::text AS poll_response_json,
    j.error_json::text AS error_json,
    j.error_code,
    j.error_message,
    j.created_at,
    j.updated_at,
    j.submitted_at,
    j.last_polled_at,
    j.completed_at
FROM dance_sell.dance_sell_jobs j
WHERE j.id = '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid
   OR j.logical_request_id = '23805c3f-1d44-465f-be30-cde61aafdbfe'
ORDER BY j.created_at;

-- ================================================================
-- B. RenderJobs
-- ================================================================
WITH target AS (
    SELECT id AS dance_sell_job_id
    FROM dance_sell.dance_sell_jobs
    WHERE id = '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid
), render_ids AS (
    SELECT render_job_id AS id
    FROM dance_sell.dance_sell_jobs j
    JOIN target t ON t.dance_sell_job_id = j.id
    WHERE render_job_id IS NOT NULL
    UNION
    SELECT reference_render_job_id
    FROM dance_sell.dance_sell_jobs j
    JOIN target t ON t.dance_sell_job_id = j.id
    WHERE reference_render_job_id IS NOT NULL
    UNION
    SELECT motion_render_job_id
    FROM dance_sell.dance_sell_jobs j
    JOIN target t ON t.dance_sell_job_id = j.id
    WHERE motion_render_job_id IS NOT NULL
    UNION
    SELECT r.id
    FROM render.render_jobs r
    JOIN target t ON r.business_entity_id = t.dance_sell_job_id
    UNION
    SELECT o.render_job_id
    FROM dance_sell.dance_sell_provider_operations o
    JOIN target t ON t.dance_sell_job_id = o.dance_sell_job_id
    WHERE o.render_job_id IS NOT NULL
)
SELECT
    r.id AS render_job_id,
    r.parent_render_job_id,
    r.retry_of_job_id,
    r.business_entity_id,
    r.job_code,
    r.job_type,
    r.operation_type,
    r.status,
    r.current_step,
    r.attempt_count,
    r.max_attempts,
    r.provider_code,
    r.model_code,
    r.provider_account_id,
    r.provider_task_id,
    r.error_code,
    r.error_message,
    r.created_at,
    r.updated_at,
    r.queued_at,
    r.started_at,
    r.completed_at,
    r.cancelled_at,
    r.input_json::text AS input_json,
    r.prompt_json::text AS prompt_json,
    r.reference_json::text AS reference_json,
    r.output_json::text AS output_json,
    r.result_summary_json::text AS result_summary_json,
    r.provider_request_json::text AS provider_request_json,
    r.provider_response_json::text AS provider_response_json,
    r.last_provider_response_json::text AS last_provider_response_json
FROM render.render_jobs r
JOIN render_ids ids ON ids.id = r.id
ORDER BY r.created_at, r.id;

-- ================================================================
-- C. ProviderOperations (do not collapse attempts)
-- ================================================================
WITH target AS (
    SELECT id AS dance_sell_job_id
    FROM dance_sell.dance_sell_jobs
    WHERE id = '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid
), render_ids AS (
    SELECT render_job_id AS id
    FROM dance_sell.dance_sell_jobs j JOIN target t ON t.dance_sell_job_id = j.id
    WHERE render_job_id IS NOT NULL
    UNION SELECT reference_render_job_id FROM dance_sell.dance_sell_jobs j JOIN target t ON t.dance_sell_job_id = j.id WHERE reference_render_job_id IS NOT NULL
    UNION SELECT motion_render_job_id FROM dance_sell.dance_sell_jobs j JOIN target t ON t.dance_sell_job_id = j.id WHERE motion_render_job_id IS NOT NULL
    UNION SELECT r.id FROM render.render_jobs r JOIN target t ON r.business_entity_id = t.dance_sell_job_id
)
SELECT
    o.id AS operation_id,
    o.parent_operation_id,
    o.render_job_id,
    o.dance_sell_job_id,
    o.operation_type,
    o.attempt_no,
    o.reference_mode,
    o.provider_code,
    o.provider_capability_id,
    o.provider_account_id,
    o.provider_model,
    o.provider_task_id,
    o.status,
    o.provider_status,
    o.request_json::text AS request_json,
    o.response_json::text AS response_json,
    o.callback_json::text AS callback_json,
    o.error_json::text AS error_json,
    o.error_code,
    o.error_message,
    o.created_at,
    o.started_at,
    o.submitted_at,
    o.completed_at,
    o.failed_at,
    o.updated_at
FROM dance_sell.dance_sell_provider_operations o
JOIN target t ON t.dance_sell_job_id = o.dance_sell_job_id
   OR o.render_job_id IN (SELECT id FROM render_ids)
ORDER BY o.created_at, o.attempt_no, o.id;

-- ================================================================
-- D. OperationAssets
-- ================================================================
WITH target AS (
    SELECT id AS dance_sell_job_id
    FROM dance_sell.dance_sell_jobs
    WHERE id = '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid
), render_ids AS (
    SELECT render_job_id AS id FROM dance_sell.dance_sell_jobs j JOIN target t ON t.dance_sell_job_id = j.id WHERE render_job_id IS NOT NULL
    UNION SELECT reference_render_job_id FROM dance_sell.dance_sell_jobs j JOIN target t ON t.dance_sell_job_id = j.id WHERE reference_render_job_id IS NOT NULL
    UNION SELECT motion_render_job_id FROM dance_sell.dance_sell_jobs j JOIN target t ON t.dance_sell_job_id = j.id WHERE motion_render_job_id IS NOT NULL
    UNION SELECT r.id FROM render.render_jobs r JOIN target t ON r.business_entity_id = t.dance_sell_job_id
), operation_ids AS (
    SELECT o.id
    FROM dance_sell.dance_sell_provider_operations o
    JOIN target t ON t.dance_sell_job_id = o.dance_sell_job_id
       OR o.render_job_id IN (SELECT id FROM render_ids)
)
SELECT
    a.id AS asset_id,
    a.operation_id,
    a.asset_role,
    a.media_id,
    a.object_key,
    a.public_url,
    a.provider_url,
    a.mime_type,
    a.metadata_json::text AS metadata_json,
    a.metadata_json->>'idBase' AS metadata_id_base,
    a.metadata_json->>'uploadUrl' AS metadata_upload_url,
    a.metadata_json->>'verificationMatched' AS verification_matched,
    a.metadata_json->>'verificationSource' AS verification_source,
    a.metadata_json->>'verifiedUrl' AS verified_url,
    a.metadata_json->>'providerStatus' AS provider_status,
    a.metadata_json->>'uploadState' AS upload_state,
    a.metadata_json->>'reusedForRenderJobId' AS reused_for_render_job_id,
    a.metadata_json->>'reusedForOperationId' AS reused_for_operation_id,
    a.created_at
FROM public.todox_ai_operation_assets a
JOIN operation_ids oi ON oi.id = a.operation_id
ORDER BY a.created_at, a.operation_id, a.asset_role;

-- ================================================================
-- E. RenderJobEvents (full chronological timeline)
-- ================================================================
WITH target AS (
    SELECT id AS dance_sell_job_id
    FROM dance_sell.dance_sell_jobs
    WHERE id = '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid
), render_ids AS (
    SELECT render_job_id AS id FROM dance_sell.dance_sell_jobs j JOIN target t ON t.dance_sell_job_id = j.id WHERE render_job_id IS NOT NULL
    UNION SELECT reference_render_job_id FROM dance_sell.dance_sell_jobs j JOIN target t ON t.dance_sell_job_id = j.id WHERE reference_render_job_id IS NOT NULL
    UNION SELECT motion_render_job_id FROM dance_sell.dance_sell_jobs j JOIN target t ON t.dance_sell_job_id = j.id WHERE motion_render_job_id IS NOT NULL
    UNION SELECT r.id FROM render.render_jobs r JOIN target t ON r.business_entity_id = t.dance_sell_job_id
    UNION SELECT o.render_job_id FROM dance_sell.dance_sell_provider_operations o JOIN target t ON t.dance_sell_job_id = o.dance_sell_job_id WHERE o.render_job_id IS NOT NULL
)
SELECT
    e.id AS event_id,
    e.job_id,
    e.render_job_id,
    e.event_type,
    e.level,
    e.message,
    e.data_json::text AS data_json,
    e.provider_code,
    e.model_code,
    e.provider_account_id,
    e.provider_task_id,
    e.created_at
FROM render.render_job_events e
JOIN render_ids ids ON ids.id = COALESCE(e.render_job_id, e.job_id)
ORDER BY e.created_at, e.id;

-- Explicit event search, using the actual event names present in the timeline.
WITH target_events AS (
    SELECT e.*
    FROM render.render_job_events e
    WHERE e.job_id IN (
        SELECT render_job_id FROM dance_sell.dance_sell_jobs
        WHERE id = '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid
    )
       OR e.render_job_id IN (
        SELECT render_job_id FROM dance_sell.dance_sell_jobs
        WHERE id = '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid
    )
       OR e.data_json::text ILIKE '%23805c3f-1d44-465f-be30-cde61aafdbfe%'
)
SELECT id AS event_id, job_id, render_job_id, event_type, level, message,
       data_json::text AS data_json, provider_task_id, created_at
FROM target_events
WHERE event_type ILIKE '%REFERENCE%'
   OR event_type ILIKE '%MOTION%'
   OR event_type ILIKE '%UPLOAD%'
   OR event_type ILIKE '%VERIFY%'
   OR event_type ILIKE '%SUBMIT%'
   OR event_type ILIKE '%POLL%'
   OR event_type ILIKE '%FAILED%'
   OR event_type ILIKE '%ERROR%'
ORDER BY created_at, id;

-- ================================================================
-- F. Media records relevant to the DanceSell job
-- ================================================================
SELECT
    m.id,
    m.file_category,
    m.file_name,
    m.file_ext,
    m.mime_type,
    m.file_size_bytes,
    m.storage_provider,
    m.object_key,
    m.file_url,
    m.public_url,
    m.width,
    m.height,
    m.checksum,
    m.metadata::text AS metadata,
    m.is_active,
    m.created_at,
    m.updated_at
FROM media.media_files m
WHERE m.id IN (
    SELECT character_media_id FROM dance_sell.dance_sell_jobs WHERE id = '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid
    UNION SELECT product_media_id FROM dance_sell.dance_sell_jobs WHERE id = '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid
    UNION SELECT direct_reference_media_id FROM dance_sell.dance_sell_jobs WHERE id = '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid
    UNION SELECT motion_media_id FROM dance_sell.dance_sell_jobs WHERE id = '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid
    UNION SELECT motion_video_media_id FROM dance_sell.dance_sell_jobs WHERE id = '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid
    UNION SELECT prepared_reference_media_id FROM dance_sell.dance_sell_jobs WHERE id = '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid
)
ORDER BY m.created_at, m.id;

ROLLBACK;
