-- RDANCE_3JOB_PRODUCTION_EXPORT.sql
-- Purpose: read-only production export for 3 RDance jobs.
-- Verified from repository migrations/source:
--   dance_sell.dance_sell_jobs
--   dance_sell.dance_sell_provider_operations
--   public.todox_ai_operation_assets
--   render.render_jobs
--   render.render_job_events
--   media.media_files
--   public.todox_ai_provider_account
--   public.todox_ai_feature_provider_route
--
-- Safety:
--   - SELECT statements only; no transaction wrapper, so one failed query does not
--     leave later exports in PostgreSQL's aborted-transaction state (SQLSTATE 25P02).
--   - No temp tables.
--   - No dynamic SQL.
--   - No CREATE/ALTER/UPDATE/DELETE/INSERT.
--   - Does not read system.ai_provider_credentials_secure.
--   - Section G sanitizes non-secret config rows by removing common secret key names.

-- 0. Target rows and actual relevant columns first.
WITH target(label, dance_sell_job_id, render_job_id, provider_task_id) AS (
    VALUES
        ('FAILED_NEW', '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2'::text),
        ('FAILED_PREVIOUS', '28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662'::text),
        ('SUCCESS', '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8'::text)
)
SELECT
    '00_TARGETS' AS section,
    t.label,
    t.dance_sell_job_id,
    t.render_job_id,
    t.provider_task_id
FROM target t
ORDER BY t.label;

SELECT
    '00_SCHEMA_COLUMNS' AS section,
    c.table_schema,
    c.table_name,
    jsonb_agg(
        jsonb_build_object(
            'ordinal_position', c.ordinal_position,
            'column_name', c.column_name,
            'data_type', c.data_type,
            'udt_name', c.udt_name,
            'is_nullable', c.is_nullable
        )
        ORDER BY c.ordinal_position
    ) AS columns_json
FROM information_schema.columns c
WHERE (c.table_schema, c.table_name) IN (
    ('dance_sell', 'dance_sell_jobs'),
    ('dance_sell', 'dance_sell_provider_operations'),
    ('public', 'todox_ai_operation_assets'),
    ('render', 'render_jobs'),
    ('render', 'render_job_events'),
    ('media', 'media_files'),
    ('public', 'todox_ai_provider_account'),
    ('public', 'todox_ai_feature_provider_route'),
    ('public', 'todox_ai_provider')
)
GROUP BY c.table_schema, c.table_name
ORDER BY c.table_schema, c.table_name;

-- A. DanceSell jobs: complete rows.
WITH target(label, dance_sell_job_id, render_job_id, provider_task_id) AS (
    VALUES
        ('FAILED_NEW', '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2'::text),
        ('FAILED_PREVIOUS', '28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662'::text),
        ('SUCCESS', '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8'::text)
)
SELECT
    'A_DANCE_SELL_JOBS' AS section,
    t.label,
    row_to_json(j) AS row_json
FROM target t
JOIN dance_sell.dance_sell_jobs j
  ON j.id = t.dance_sell_job_id
ORDER BY t.label, j.created_at;

-- B. Render jobs: target render jobs plus retry attempts linked by retry_of_job_id.
WITH target(label, dance_sell_job_id, render_job_id, provider_task_id) AS (
    VALUES
        ('FAILED_NEW', '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2'::text),
        ('FAILED_PREVIOUS', '28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662'::text),
        ('SUCCESS', '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8'::text)
),
render_scope AS (
    SELECT DISTINCT r.*
    FROM render.render_jobs r
    WHERE r.id IN (SELECT render_job_id FROM target)
       OR r.retry_of_job_id IN (SELECT render_job_id FROM target)
       OR r.id IN (
            SELECT j.render_job_id
            FROM dance_sell.dance_sell_jobs j
            JOIN target t ON t.dance_sell_job_id = j.id
            WHERE j.render_job_id IS NOT NULL
       )
)
SELECT
    'B_RENDER_JOBS' AS section,
    COALESCE(t.label, tj.label, rt.label, 'RELATED_RENDER') AS target_label,
    CASE
        WHEN r.id = t.render_job_id THEN 'target_render_job'
        WHEN r.id = tj.render_job_id THEN 'dance_job_render_job'
        WHEN r.retry_of_job_id = rt.render_job_id THEN 'retry_of_target_render_job'
        ELSE 'related_render_job'
    END AS relationship,
    row_to_json(r) AS row_json
FROM render_scope r
LEFT JOIN target t ON t.render_job_id = r.id
LEFT JOIN dance_sell.dance_sell_jobs j ON j.render_job_id = r.id
LEFT JOIN target tj ON tj.dance_sell_job_id = j.id
LEFT JOIN target rt ON rt.render_job_id = r.retry_of_job_id
ORDER BY target_label, r.queued_at NULLS LAST, r.created_at NULLS LAST, r.id;

-- C. Provider operations: all operations associated with target dance jobs, target render jobs, or target provider task ids.
WITH target(label, dance_sell_job_id, render_job_id, provider_task_id) AS (
    VALUES
        ('FAILED_NEW', '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2'::text),
        ('FAILED_PREVIOUS', '28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662'::text),
        ('SUCCESS', '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8'::text)
),
op_scope AS (
    SELECT DISTINCT o.*
    FROM dance_sell.dance_sell_provider_operations o
    WHERE o.dance_sell_job_id IN (SELECT dance_sell_job_id FROM target)
       OR o.render_job_id IN (SELECT render_job_id FROM target)
       OR o.provider_task_id IN (SELECT provider_task_id FROM target)
)
SELECT
    'C_PROVIDER_OPERATIONS' AS section,
    COALESCE(t_job.label, t_render.label, t_task.label, 'RELATED_OPERATION') AS target_label,
    row_to_json(o) AS row_json
FROM op_scope o
LEFT JOIN target t_job ON t_job.dance_sell_job_id = o.dance_sell_job_id
LEFT JOIN target t_render ON t_render.render_job_id = o.render_job_id
LEFT JOIN target t_task ON t_task.provider_task_id = o.provider_task_id
ORDER BY target_label, o.operation_type, o.attempt_no, o.created_at, o.id;

-- D. Operation assets joined via provider operations: complete asset rows plus critical metadata.
WITH target(label, dance_sell_job_id, render_job_id, provider_task_id) AS (
    VALUES
        ('FAILED_NEW', '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2'::text),
        ('FAILED_PREVIOUS', '28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662'::text),
        ('SUCCESS', '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8'::text)
),
op_scope AS (
    SELECT DISTINCT o.*
    FROM dance_sell.dance_sell_provider_operations o
    WHERE o.dance_sell_job_id IN (SELECT dance_sell_job_id FROM target)
       OR o.render_job_id IN (SELECT render_job_id FROM target)
       OR o.provider_task_id IN (SELECT provider_task_id FROM target)
)
SELECT
    'D_OPERATION_ASSETS' AS section,
    COALESCE(t_job.label, t_render.label, t_task.label, 'RELATED_OPERATION') AS target_label,
    o.id AS operation_id,
    o.operation_type,
    o.attempt_no,
    o.provider_code,
    o.provider_model,
    o.provider_task_id,
    a.asset_role,
    a.media_id,
    a.object_key,
    a.public_url,
    a.provider_url,
    a.mime_type,
    a.metadata_json,
    row_to_json(a) AS asset_row_json
FROM op_scope o
JOIN public.todox_ai_operation_assets a
  ON a.operation_id = o.id
LEFT JOIN target t_job ON t_job.dance_sell_job_id = o.dance_sell_job_id
LEFT JOIN target t_render ON t_render.render_job_id = o.render_job_id
LEFT JOIN target t_task ON t_task.provider_task_id = o.provider_task_id
ORDER BY target_label, o.operation_type, o.attempt_no, a.asset_role, a.created_at, a.id;

-- E. Render events: all events for scoped render jobs, ordered by confirmed event FK job_id and created_at.
WITH target(label, dance_sell_job_id, render_job_id, provider_task_id) AS (
    VALUES
        ('FAILED_NEW', '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2'::text),
        ('FAILED_PREVIOUS', '28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662'::text),
        ('SUCCESS', '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8'::text)
),
render_scope AS (
    SELECT DISTINCT r.id
    FROM render.render_jobs r
    WHERE r.id IN (SELECT render_job_id FROM target)
       OR r.retry_of_job_id IN (SELECT render_job_id FROM target)
       OR r.id IN (
            SELECT j.render_job_id
            FROM dance_sell.dance_sell_jobs j
            JOIN target t ON t.dance_sell_job_id = j.id
            WHERE j.render_job_id IS NOT NULL
       )
)
SELECT
    'E_RENDER_JOB_EVENTS' AS section,
    t.label AS target_label,
    e.job_id,
    e.event_type,
    e.level,
    e.message,
    e.data_json,
    e.provider_code,
    e.model_code,
    e.created_at,
    row_to_json(e) AS event_row_json
FROM render.render_job_events e
JOIN render_scope rs ON rs.id = e.job_id
LEFT JOIN target t ON t.render_job_id = e.job_id
ORDER BY e.job_id, e.created_at, e.event_type, e.id;

-- F. Media files referenced by actual media-id columns on the DanceSell job rows and operation assets.
WITH target(label, dance_sell_job_id, render_job_id, provider_task_id) AS (
    VALUES
        ('FAILED_NEW', '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2'::text),
        ('FAILED_PREVIOUS', '28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662'::text),
        ('SUCCESS', '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8'::text)
),
job_media AS (
    SELECT t.label, 'character_media_id' AS source_column, j.character_media_id AS media_id
    FROM target t JOIN dance_sell.dance_sell_jobs j ON j.id = t.dance_sell_job_id
    WHERE j.character_media_id IS NOT NULL
    UNION ALL
    SELECT t.label, 'product_media_id', j.product_media_id
    FROM target t JOIN dance_sell.dance_sell_jobs j ON j.id = t.dance_sell_job_id
    WHERE j.product_media_id IS NOT NULL
    UNION ALL
    SELECT t.label, 'direct_reference_media_id', j.direct_reference_media_id
    FROM target t JOIN dance_sell.dance_sell_jobs j ON j.id = t.dance_sell_job_id
    WHERE j.direct_reference_media_id IS NOT NULL
    UNION ALL
    SELECT t.label, 'motion_video_media_id', j.motion_video_media_id
    FROM target t JOIN dance_sell.dance_sell_jobs j ON j.id = t.dance_sell_job_id
    WHERE j.motion_video_media_id IS NOT NULL
    UNION ALL
    SELECT t.label, 'prepared_reference_media_id', j.prepared_reference_media_id
    FROM target t JOIN dance_sell.dance_sell_jobs j ON j.id = t.dance_sell_job_id
    WHERE j.prepared_reference_media_id IS NOT NULL
),
op_scope AS (
    SELECT DISTINCT o.*
    FROM dance_sell.dance_sell_provider_operations o
    WHERE o.dance_sell_job_id IN (SELECT dance_sell_job_id FROM target)
       OR o.render_job_id IN (SELECT render_job_id FROM target)
       OR o.provider_task_id IN (SELECT provider_task_id FROM target)
),
asset_media AS (
    SELECT
        COALESCE(t_job.label, t_render.label, t_task.label, 'RELATED_OPERATION') AS label,
        'operation_asset:' || a.asset_role AS source_column,
        a.media_id
    FROM op_scope o
    JOIN public.todox_ai_operation_assets a ON a.operation_id = o.id
    LEFT JOIN target t_job ON t_job.dance_sell_job_id = o.dance_sell_job_id
    LEFT JOIN target t_render ON t_render.render_job_id = o.render_job_id
    LEFT JOIN target t_task ON t_task.provider_task_id = o.provider_task_id
    WHERE a.media_id IS NOT NULL
),
media_scope AS (
    SELECT DISTINCT label, source_column, media_id FROM job_media
    UNION
    SELECT DISTINCT label, source_column, media_id FROM asset_media
)
SELECT
    'F_MEDIA_FILES' AS section,
    ms.label AS target_label,
    ms.source_column,
    row_to_json(m) AS media_row_json
FROM media_scope ms
JOIN media.media_files m ON m.id = ms.media_id
ORDER BY ms.label, ms.source_column, m.created_at, m.id;

-- G. Non-secret runtime config/provider context.
-- Historical values that are not persisted by the job/operation are exposed as NULL.
-- Runtime resolution in source uses route config, provider account config, provider base_url/config,
-- then code fallback for 79AI/Gommo endpoints.
WITH target(label, dance_sell_job_id, render_job_id, provider_task_id) AS (
    VALUES
        ('FAILED_NEW', '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2'::text),
        ('FAILED_PREVIOUS', '28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662'::text),
        ('SUCCESS', '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8'::text)
),
job_scope AS (
    SELECT t.label, j.*
    FROM target t
    JOIN dance_sell.dance_sell_jobs j ON j.id = t.dance_sell_job_id
),
op_scope AS (
    SELECT DISTINCT o.*
    FROM dance_sell.dance_sell_provider_operations o
    WHERE o.dance_sell_job_id IN (SELECT dance_sell_job_id FROM target)
       OR o.render_job_id IN (SELECT render_job_id FROM target)
       OR o.provider_task_id IN (SELECT provider_task_id FROM target)
),
route_scope AS (
    SELECT DISTINCT r.*
    FROM public.todox_ai_feature_provider_route r
    WHERE r.feature_code = 'dance_sell'
      AND r.operation_type IN ('reference_image', 'motion_video')
      AND (
            (r.provider_code, r.model_name) IN (
                SELECT reference_provider_code, reference_provider_model
                FROM job_scope
                WHERE reference_provider_code IS NOT NULL AND reference_provider_model IS NOT NULL
                UNION
                SELECT motion_provider_code, motion_provider_model
                FROM job_scope
                WHERE motion_provider_code IS NOT NULL AND motion_provider_model IS NOT NULL
                UNION
                SELECT provider_code, provider_model
                FROM op_scope
                WHERE provider_code IS NOT NULL AND provider_model IS NOT NULL
            )
         OR (
                NOT EXISTS (
                    SELECT 1 FROM job_scope js
                    WHERE (js.reference_provider_code IS NOT NULL AND js.reference_provider_model IS NOT NULL)
                       OR (js.motion_provider_code IS NOT NULL AND js.motion_provider_model IS NOT NULL)
                )
                AND NOT EXISTS (
                    SELECT 1 FROM op_scope o
                    WHERE o.provider_code IS NOT NULL AND o.provider_model IS NOT NULL
                )
            )
      )
),
account_scope AS (
    SELECT DISTINCT a.*
    FROM public.todox_ai_provider_account a
    WHERE a.id IN (
        SELECT reference_provider_account_id FROM job_scope WHERE reference_provider_account_id IS NOT NULL
        UNION
        SELECT motion_provider_account_id FROM job_scope WHERE motion_provider_account_id IS NOT NULL
        UNION
        SELECT provider_account_id FROM op_scope WHERE provider_account_id IS NOT NULL
    )
       OR a.provider_code IN (
        SELECT reference_provider_code FROM job_scope WHERE reference_provider_code IS NOT NULL
        UNION
        SELECT motion_provider_code FROM job_scope WHERE motion_provider_code IS NOT NULL
        UNION
        SELECT provider_code FROM op_scope WHERE provider_code IS NOT NULL
    )
),
provider_scope AS (
    SELECT DISTINCT p.*
    FROM public.todox_ai_provider p
    WHERE p.provider_code IN (
        SELECT reference_provider_code FROM job_scope WHERE reference_provider_code IS NOT NULL
        UNION
        SELECT motion_provider_code FROM job_scope WHERE motion_provider_code IS NOT NULL
        UNION
        SELECT provider_code FROM op_scope WHERE provider_code IS NOT NULL
        UNION
        SELECT provider_code FROM route_scope WHERE provider_code IS NOT NULL
        UNION
        SELECT provider_code FROM account_scope WHERE provider_code IS NOT NULL
    )
),
secret_keys AS (
    SELECT ARRAY[
        'access_token',
        'api_key',
        'apikey',
        'authorization',
        'authorization_header',
        'auth_header',
        'bearer',
        'ciphertext',
        'client_secret',
        'credential',
        'credential_config_name',
        'encrypted',
        'encrypted_secret',
        'key',
        'nonce',
        'password',
        'refresh_token',
        'secret',
        'token'
    ]::text[] AS keys
)
SELECT
    'G_RUNTIME_CONFIG_JOB_VALUES' AS section,
    js.label AS target_label,
    js.id AS dance_sell_job_id,
    js.provider_code AS legacy_provider_code,
    js.provider_model AS legacy_provider_model,
    js.reference_provider_code,
    js.reference_provider_model,
    js.reference_provider_capability_id,
    js.reference_provider_account_id,
    js.motion_provider_code,
    js.motion_provider_model,
    js.motion_provider_capability_id,
    js.motion_provider_account_id,
    js.mode AS business_mode,
    js.request_json ->> 'ratio' AS request_ratio,
    js.request_json ->> 'autoFinish' AS request_auto_finish,
    js.request_json ->> 'background_source' AS request_background_source,
    js.request_json ->> 'include_images_zero_url' AS request_include_images_zero_url,
    (to_jsonb(js) - 'label') AS dance_sell_job_row_json
FROM job_scope js
ORDER BY js.label
;

WITH target(label, dance_sell_job_id, render_job_id, provider_task_id) AS (
    VALUES
        ('FAILED_NEW', '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2'::text),
        ('FAILED_PREVIOUS', '28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662'::text),
        ('SUCCESS', '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8'::text)
),
job_scope AS (
    SELECT t.label, j.*
    FROM target t
    JOIN dance_sell.dance_sell_jobs j ON j.id = t.dance_sell_job_id
),
op_scope AS (
    SELECT DISTINCT o.*
    FROM dance_sell.dance_sell_provider_operations o
    WHERE o.dance_sell_job_id IN (SELECT dance_sell_job_id FROM target)
       OR o.render_job_id IN (SELECT render_job_id FROM target)
       OR o.provider_task_id IN (SELECT provider_task_id FROM target)
),
route_scope AS (
    SELECT DISTINCT r.*
    FROM public.todox_ai_feature_provider_route r
    WHERE r.feature_code = 'dance_sell'
      AND r.operation_type IN ('reference_image', 'motion_video')
      AND (
            (r.provider_code, r.model_name) IN (
                SELECT reference_provider_code, reference_provider_model
                FROM job_scope
                WHERE reference_provider_code IS NOT NULL AND reference_provider_model IS NOT NULL
                UNION
                SELECT motion_provider_code, motion_provider_model
                FROM job_scope
                WHERE motion_provider_code IS NOT NULL AND motion_provider_model IS NOT NULL
                UNION
                SELECT provider_code, provider_model
                FROM op_scope
                WHERE provider_code IS NOT NULL AND provider_model IS NOT NULL
            )
         OR (
                NOT EXISTS (
                    SELECT 1 FROM job_scope js
                    WHERE (js.reference_provider_code IS NOT NULL AND js.reference_provider_model IS NOT NULL)
                       OR (js.motion_provider_code IS NOT NULL AND js.motion_provider_model IS NOT NULL)
                )
                AND NOT EXISTS (
                    SELECT 1 FROM op_scope o
                    WHERE o.provider_code IS NOT NULL AND o.provider_model IS NOT NULL
                )
            )
      )
),
secret_keys AS (
    SELECT ARRAY[
        'access_token',
        'api_key',
        'apikey',
        'authorization',
        'authorization_header',
        'auth_header',
        'bearer',
        'ciphertext',
        'client_secret',
        'credential',
        'encrypted',
        'encrypted_secret',
        'key',
        'nonce',
        'password',
        'refresh_token',
        'secret',
        'token'
    ]::text[] AS keys
)
SELECT
    'G_RUNTIME_CONFIG_ROUTES' AS section,
    r.feature_code,
    r.operation_type,
    r.provider_code,
    NULL::uuid AS provider_account_id_not_persisted_on_route_schema,
    r.model_mode,
    r.route_priority,
    r.is_default,
    r.enabled,
    r.fallback_on,
    r.model_name,
    r.config_json ->> 'base_url' AS base_url,
    r.config_json ->> 'list_base_url' AS list_base_url,
    r.config_json ->> 'domain' AS domain,
    r.config_json ->> 'project_id' AS project_id,
    r.config_json ->> 'subType' AS subtype,
    r.config_json ->> 'sub_type' AS sub_type,
    r.config_json ->> 'background_source' AS background_source,
    r.config_json ->> 'mode' AS provider_mode,
    r.config_json ->> 'provider_mode' AS provider_mode_alt,
    r.config_json ->> 'ratio' AS provider_ratio,
    r.config_json ->> 'include_images_zero_url' AS include_images_zero_url,
    (to_jsonb(r) || jsonb_build_object(
        'config_json',
        r.config_json
            - 'access_token'
            - 'api_key'
            - 'apikey'
            - 'authorization'
            - 'authorization_header'
            - 'auth_header'
            - 'bearer'
            - 'ciphertext'
            - 'client_secret'
            - 'credential'
            - 'encrypted'
            - 'encrypted_secret'
            - 'key'
            - 'nonce'
            - 'password'
            - 'refresh_token'
            - 'secret'
            - 'token'
    )) AS sanitized_route_row_json
FROM route_scope r
ORDER BY r.operation_type, r.route_priority, r.provider_code, r.model_name, r.id
;

WITH target(label, dance_sell_job_id, render_job_id, provider_task_id) AS (
    VALUES
        ('FAILED_NEW', '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2'::text),
        ('FAILED_PREVIOUS', '28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662'::text),
        ('SUCCESS', '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8'::text)
),
job_scope AS (
    SELECT t.label, j.*
    FROM target t
    JOIN dance_sell.dance_sell_jobs j ON j.id = t.dance_sell_job_id
),
op_scope AS (
    SELECT DISTINCT o.*
    FROM dance_sell.dance_sell_provider_operations o
    WHERE o.dance_sell_job_id IN (SELECT dance_sell_job_id FROM target)
       OR o.render_job_id IN (SELECT render_job_id FROM target)
       OR o.provider_task_id IN (SELECT provider_task_id FROM target)
),
route_scope AS (
    SELECT DISTINCT r.*
    FROM public.todox_ai_feature_provider_route r
    WHERE r.feature_code = 'dance_sell'
      AND r.operation_type IN ('reference_image', 'motion_video')
      AND (
            (r.provider_code, r.model_name) IN (
                SELECT reference_provider_code, reference_provider_model
                FROM job_scope
                WHERE reference_provider_code IS NOT NULL AND reference_provider_model IS NOT NULL
                UNION
                SELECT motion_provider_code, motion_provider_model
                FROM job_scope
                WHERE motion_provider_code IS NOT NULL AND motion_provider_model IS NOT NULL
                UNION
                SELECT provider_code, provider_model
                FROM op_scope
                WHERE provider_code IS NOT NULL AND provider_model IS NOT NULL
            )
         OR (
                NOT EXISTS (
                    SELECT 1 FROM job_scope js
                    WHERE (js.reference_provider_code IS NOT NULL AND js.reference_provider_model IS NOT NULL)
                       OR (js.motion_provider_code IS NOT NULL AND js.motion_provider_model IS NOT NULL)
                )
                AND NOT EXISTS (
                    SELECT 1 FROM op_scope o
                    WHERE o.provider_code IS NOT NULL AND o.provider_model IS NOT NULL
                )
            )
      )
),
account_scope AS (
    SELECT DISTINCT a.*
    FROM public.todox_ai_provider_account a
    WHERE a.id IN (
        SELECT reference_provider_account_id FROM job_scope WHERE reference_provider_account_id IS NOT NULL
        UNION
        SELECT motion_provider_account_id FROM job_scope WHERE motion_provider_account_id IS NOT NULL
        UNION
        SELECT provider_account_id FROM op_scope WHERE provider_account_id IS NOT NULL
    )
       OR a.provider_code IN (
        SELECT reference_provider_code FROM job_scope WHERE reference_provider_code IS NOT NULL
        UNION
        SELECT motion_provider_code FROM job_scope WHERE motion_provider_code IS NOT NULL
        UNION
        SELECT provider_code FROM op_scope WHERE provider_code IS NOT NULL
    )
),
secret_keys AS (
    SELECT ARRAY[
        'access_token',
        'api_key',
        'apikey',
        'authorization',
        'authorization_header',
        'auth_header',
        'bearer',
        'ciphertext',
        'client_secret',
        'credential',
        'credential_config_name',
        'encrypted',
        'encrypted_secret',
        'key',
        'nonce',
        'password',
        'refresh_token',
        'secret',
        'token'
    ]::text[] AS keys
)
SELECT
    'G_RUNTIME_CONFIG_PROVIDER_ACCOUNTS' AS section,
    a.id AS provider_account_id,
    a.provider_code,
    a.account_name,
    a.environment,
    a.enabled,
    a.is_default,
    a.config_json ->> 'base_url' AS base_url,
    a.config_json ->> 'list_base_url' AS list_base_url,
    a.config_json ->> 'domain' AS domain,
    a.config_json ->> 'project_id' AS project_id,
    a.config_json ->> 'subType' AS subtype,
    a.config_json ->> 'sub_type' AS sub_type,
    a.config_json ->> 'background_source' AS background_source,
    a.config_json ->> 'mode' AS provider_mode,
    a.config_json ->> 'ratio' AS provider_ratio,
    a.config_json ->> 'include_images_zero_url' AS include_images_zero_url,
    (to_jsonb(a) - 'credential_config_name' || jsonb_build_object(
        'config_json',
        a.config_json
            - 'access_token'
            - 'api_key'
            - 'apikey'
            - 'authorization'
            - 'authorization_header'
            - 'auth_header'
            - 'bearer'
            - 'ciphertext'
            - 'client_secret'
            - 'credential'
            - 'credential_config_name'
            - 'encrypted'
            - 'encrypted_secret'
            - 'key'
            - 'nonce'
            - 'password'
            - 'refresh_token'
            - 'secret'
            - 'token'
    )) AS sanitized_account_row_json
FROM account_scope a
ORDER BY a.provider_code, a.environment, a.is_default DESC, a.account_name, a.id
;

WITH target(label, dance_sell_job_id, render_job_id, provider_task_id) AS (
    VALUES
        ('FAILED_NEW', '23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2'::text),
        ('FAILED_PREVIOUS', '28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662'::text),
        ('SUCCESS', '49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8'::text)
),
job_scope AS (
    SELECT t.label, j.*
    FROM target t
    JOIN dance_sell.dance_sell_jobs j ON j.id = t.dance_sell_job_id
),
op_scope AS (
    SELECT DISTINCT o.*
    FROM dance_sell.dance_sell_provider_operations o
    WHERE o.dance_sell_job_id IN (SELECT dance_sell_job_id FROM target)
       OR o.render_job_id IN (SELECT render_job_id FROM target)
       OR o.provider_task_id IN (SELECT provider_task_id FROM target)
),
route_scope AS (
    SELECT DISTINCT r.*
    FROM public.todox_ai_feature_provider_route r
    WHERE r.feature_code = 'dance_sell'
      AND r.operation_type IN ('reference_image', 'motion_video')
      AND (
            (r.provider_code, r.model_name) IN (
                SELECT reference_provider_code, reference_provider_model
                FROM job_scope
                WHERE reference_provider_code IS NOT NULL AND reference_provider_model IS NOT NULL
                UNION
                SELECT motion_provider_code, motion_provider_model
                FROM job_scope
                WHERE motion_provider_code IS NOT NULL AND motion_provider_model IS NOT NULL
                UNION
                SELECT provider_code, provider_model
                FROM op_scope
                WHERE provider_code IS NOT NULL AND provider_model IS NOT NULL
            )
         OR (
                NOT EXISTS (
                    SELECT 1 FROM job_scope js
                    WHERE (js.reference_provider_code IS NOT NULL AND js.reference_provider_model IS NOT NULL)
                       OR (js.motion_provider_code IS NOT NULL AND js.motion_provider_model IS NOT NULL)
                )
                AND NOT EXISTS (
                    SELECT 1 FROM op_scope o
                    WHERE o.provider_code IS NOT NULL AND o.provider_model IS NOT NULL
                )
            )
      )
),
account_scope AS (
    SELECT DISTINCT a.*
    FROM public.todox_ai_provider_account a
    WHERE a.id IN (
        SELECT reference_provider_account_id FROM job_scope WHERE reference_provider_account_id IS NOT NULL
        UNION
        SELECT motion_provider_account_id FROM job_scope WHERE motion_provider_account_id IS NOT NULL
        UNION
        SELECT provider_account_id FROM op_scope WHERE provider_account_id IS NOT NULL
    )
       OR a.provider_code IN (
        SELECT reference_provider_code FROM job_scope WHERE reference_provider_code IS NOT NULL
        UNION
        SELECT motion_provider_code FROM job_scope WHERE motion_provider_code IS NOT NULL
        UNION
        SELECT provider_code FROM op_scope WHERE provider_code IS NOT NULL
    )
),
provider_scope AS (
    SELECT DISTINCT p.*
    FROM public.todox_ai_provider p
    WHERE p.provider_code IN (
        SELECT reference_provider_code FROM job_scope WHERE reference_provider_code IS NOT NULL
        UNION
        SELECT motion_provider_code FROM job_scope WHERE motion_provider_code IS NOT NULL
        UNION
        SELECT provider_code FROM op_scope WHERE provider_code IS NOT NULL
        UNION
        SELECT provider_code FROM route_scope WHERE provider_code IS NOT NULL
        UNION
        SELECT provider_code FROM account_scope WHERE provider_code IS NOT NULL
    )
),
secret_keys AS (
    SELECT ARRAY[
        'access_token',
        'api_key',
        'apikey',
        'api_key_config_name',
        'authorization',
        'authorization_header',
        'auth_header',
        'bearer',
        'ciphertext',
        'client_secret',
        'credential',
        'encrypted',
        'encrypted_secret',
        'key',
        'nonce',
        'password',
        'refresh_token',
        'secret',
        'token'
    ]::text[] AS keys
)
SELECT
    'G_RUNTIME_CONFIG_PROVIDERS' AS section,
    p.provider_code,
    p.provider_name,
    p.provider_type,
    p.base_url,
    p.enabled,
    p.priority,
    p.config_json ->> 'base_url' AS config_base_url,
    p.config_json ->> 'list_base_url' AS list_base_url,
    p.config_json ->> 'domain' AS domain,
    p.config_json ->> 'project_id' AS project_id,
    (to_jsonb(p) - 'api_key_config_name' || jsonb_build_object(
        'config_json',
        p.config_json
            - 'access_token'
            - 'api_key'
            - 'apikey'
            - 'api_key_config_name'
            - 'authorization'
            - 'authorization_header'
            - 'auth_header'
            - 'bearer'
            - 'ciphertext'
            - 'client_secret'
            - 'credential'
            - 'encrypted'
            - 'encrypted_secret'
            - 'key'
            - 'nonce'
            - 'password'
            - 'refresh_token'
            - 'secret'
            - 'token'
    )) AS sanitized_provider_row_json
FROM provider_scope p
ORDER BY p.provider_code, p.priority, p.provider_name
;

-- No historical provider fallback values are persisted when route/account/provider rows omit them.
-- Source fallback observed in DanceSellRenderHandler/Ai79TaskClient path:
--   base_url fallback: https://api.gommo.net/ai
--   domain fallback: 79ai.net
--   submit_path default: /create-video
--   poll_path default: /video
--   motion payload fields: domain, project_id, model, prompt, image_url, video_url,
--                          subType, background_source, mode, ratio, images[0][url]
