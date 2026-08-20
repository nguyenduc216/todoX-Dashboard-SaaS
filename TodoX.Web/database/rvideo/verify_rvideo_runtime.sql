-- RVIDEO 79AI runtime verification.
-- Read-only: this script must not change data.

DO $$
DECLARE
    missing text[];
    bad_routes text;
    image_cap_id bigint;
    video_cap_id bigint;
    endpoint_ok boolean;
    pricing_ok boolean;
    credential_ok boolean;
    requirement_count int;
    veo_omni_flash_count int;
    veo_31_fast_count int;
    veo_31_lite_count int;
    grok_normal_count int;
    current_policy_count int;
BEGIN
    SELECT array_agg(format('%I.%I', schema_name, table_name))
      INTO missing
      FROM (VALUES
            ('video_render', 'scene_image_versions'),
            ('video_render', 'scene_video_versions'),
            ('billing', 'ai_image_billing_records'),
            ('billing', 'ai_image_provider_attempts'),
            ('billing', 'token_wallets'),
            ('billing', 'token_transactions'),
            ('public', 'todox_ai_provider'),
            ('public', 'todox_ai_provider_capability'),
            ('public', 'todox_ai_provider_model'),
            ('public', 'todox_ai_model_price'),
            ('public', 'todox_ai_provider_account'),
            ('public', 'todox_ai_provider_account_credential'),
            ('system', 'ai_provider_credentials_secure')) AS required(schema_name, table_name)
     WHERE to_regclass(format('%I.%I', schema_name, table_name)) IS NULL;

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'RVIDEO_RUNTIME_VERIFY_FAILED missing database objects: %', array_to_string(missing, ', ');
    END IF;

    SELECT array_agg(column_name)
      INTO missing
      FROM (VALUES
            ('project_id'), ('scene_id'), ('version_number'), ('logical_request_id'),
            ('provider_code'), ('requested_model'), ('actual_model'), ('provider_task_id'),
            ('compiled_image_prompt_snapshot'), ('provider_usage_json'), ('status'),
            ('result_media_id'), ('public_url'), ('created_at'), ('updated_at')) AS required(column_name)
     WHERE NOT EXISTS (
            SELECT 1
              FROM information_schema.columns
             WHERE table_schema='video_render'
               AND table_name='scene_image_versions'
               AND column_name=required.column_name);

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'RVIDEO_RUNTIME_VERIFY_FAILED scene_image_versions missing columns: %', array_to_string(missing, ', ');
    END IF;

    SELECT array_agg(column_name)
      INTO missing
      FROM (VALUES
            ('project_id'), ('scene_id'), ('source_image_version_id'), ('version_number'),
            ('logical_request_id'), ('render_job_id'), ('provider_code'), ('requested_model'),
            ('actual_model'), ('provider_capability_id'), ('provider_task_id'), ('result_media_id'), ('storage_key'),
            ('public_url'), ('duration_seconds'), ('billing_logical_request_id'), ('status'),
            ('error_code'), ('error_message'), ('submitted_at'), ('completed_at'),
            ('created_at'), ('updated_at')) AS required(column_name)
     WHERE NOT EXISTS (
            SELECT 1
              FROM information_schema.columns
             WHERE table_schema='video_render'
               AND table_name='scene_video_versions'
               AND column_name=required.column_name);

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'RVIDEO_RUNTIME_VERIFY_FAILED scene_video_versions missing columns: %', array_to_string(missing, ', ');
    END IF;

    SELECT array_agg(column_name)
      INTO missing
      FROM (VALUES
            ('logical_request_id'), ('render_job_id'), ('customer_id'), ('user_id'),
            ('provider_id'), ('provider_capability_id'), ('provider_code'), ('capability_code'),
            ('feature_code'), ('requested_model'), ('actual_model'), ('provider_task_id'),
            ('customer_charged_points'), ('status'), ('error_message'), ('tariff_snapshot_json'),
            ('metadata_json'), ('reserved_until'), ('completed_at'), ('failed_at'),
            ('created_at'), ('updated_at')) AS required(column_name)
     WHERE NOT EXISTS (
            SELECT 1
              FROM information_schema.columns
             WHERE table_schema='billing'
               AND table_name='ai_image_billing_records'
               AND column_name=required.column_name);

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'RVIDEO_RUNTIME_VERIFY_FAILED ai_image_billing_records missing columns: %', array_to_string(missing, ', ');
    END IF;

    SELECT array_agg(column_name)
      INTO missing
      FROM (VALUES
            ('billing_record_id'), ('attempt_number'), ('model_name'), ('provider_task_id'),
            ('success'), ('provider_estimated_cost_usd'), ('provider_actual_cost_usd'),
            ('cost_source'), ('error_code'), ('error_message'), ('raw_usage_json'),
            ('started_at'), ('completed_at'), ('created_at')) AS required(column_name)
     WHERE NOT EXISTS (
            SELECT 1
              FROM information_schema.columns
             WHERE table_schema='billing'
               AND table_name='ai_image_provider_attempts'
               AND column_name=required.column_name);

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'RVIDEO_RUNTIME_VERIFY_FAILED ai_image_provider_attempts missing columns: %', array_to_string(missing, ', ');
    END IF;

    SELECT c.id
      INTO image_cap_id
      FROM public.todox_ai_provider_capability c
      JOIN public.todox_ai_provider p ON p.id = c.provider_id
     WHERE lower(btrim(c.capability_code))='rvideo_scene_image_generation'
       AND lower(btrim(p.provider_code))='79ai'
       AND p.enabled = true
       AND c.enabled = true
     ORDER BY c.is_default DESC, c.id
     LIMIT 1;

    IF image_cap_id IS NULL THEN
        RAISE EXCEPTION 'RVIDEO_RUNTIME_VERIFY_FAILED enabled 79AI image capability not found.';
    END IF;

    SELECT c.id
      INTO video_cap_id
      FROM public.todox_ai_provider_capability c
      JOIN public.todox_ai_provider p ON p.id = c.provider_id
     WHERE lower(btrim(c.capability_code))='rvideo_scene_video_generation'
       AND lower(btrim(p.provider_code))='79ai'
       AND p.enabled = true
       AND c.enabled = true
     ORDER BY c.is_default DESC, c.id
     LIMIT 1;

    IF video_cap_id IS NULL THEN
        RAISE EXCEPTION 'RVIDEO_RUNTIME_VERIFY_FAILED enabled 79AI video capability not found.';
    END IF;

    SELECT string_agg(format('%s/%s', p.provider_code, c.id), ', ')
      INTO bad_routes
      FROM public.todox_ai_provider_capability c
      JOIN public.todox_ai_provider p ON p.id = c.provider_id
     WHERE lower(btrim(c.capability_code))='rvideo_scene_video_generation'
       AND c.enabled = true
       AND p.enabled = true
       AND lower(btrim(p.provider_code)) <> '79ai';

    IF bad_routes IS NOT NULL THEN
        RAISE EXCEPTION 'RVIDEO_RUNTIME_VERIFY_FAILED non-79AI active video route(s): %', bad_routes;
    END IF;

    SELECT EXISTS (
        SELECT 1
          FROM public.todox_ai_provider_capability c
          JOIN public.todox_ai_provider p ON p.id = c.provider_id
         WHERE c.id = video_cap_id
           AND COALESCE(c.config_json->>'image_upload_path', p.config_json->>'image_upload_path', '/image-upload') = '/image-upload'
           AND COALESCE(c.config_json->>'submit_path', p.config_json->>'video_submit_path', c.endpoint_path, '/create-video') = '/create-video'
           AND COALESCE(c.config_json->>'poll_path', p.config_json->>'video_poll_path', '/video') = '/video'
    ) INTO endpoint_ok;

    IF NOT endpoint_ok THEN
        RAISE EXCEPTION 'RVIDEO_RUNTIME_VERIFY_FAILED endpoint contract mismatch; expected /image-upload, /create-video, /video.';
    END IF;

    SELECT c.unit_cost_points > 0
           AND jsonb_typeof(c.config_json->'pricing'->'rules') = 'array'
           AND jsonb_array_length(c.config_json->'pricing'->'rules') > 0
      INTO pricing_ok
      FROM public.todox_ai_provider_capability c
     WHERE c.id = video_cap_id;

    IF COALESCE(pricing_ok, false) = false THEN
        RAISE EXCEPTION 'RVIDEO_RUNTIME_VERIFY_FAILED active 79AI video capability has no positive unit cost and catalog pricing rules.';
    END IF;

    SELECT EXISTS (
        SELECT 1
          FROM public.todox_ai_provider_account a
          JOIN public.todox_ai_provider_account_credential m ON m.provider_account_id = a.id
          JOIN system.ai_provider_credentials_secure s ON s.id = m.secure_credential_id
         WHERE lower(btrim(a.provider_code)) = '79ai'
           AND a.environment = 'production'
           AND a.enabled = true
           AND m.credential_role = 'access_token'
           AND m.enabled = true
           AND s.status = 'active'
           AND s.valid_from <= now()
           AND (s.expires_at IS NULL OR s.expires_at > now())
    ) INTO credential_ok;

    IF NOT credential_ok THEN
        RAISE EXCEPTION 'RVIDEO_RUNTIME_VERIFY_FAILED active 79AI production access_token mapping not found.';
    END IF;

    SELECT count(*)
      INTO current_policy_count
      FROM public.todox_ai_provider_model
     WHERE lower(btrim(provider_code))='79ai'
       AND lower(provider_model_code) IN ('seedance_20_pro', 'seedance_25_omni')
       AND enabled = true
       AND is_deprecated = false;

    IF current_policy_count < 2 THEN
        RAISE EXCEPTION 'RVIDEO_RUNTIME_VERIFY_FAILED current Seedance policy is not fully present in ai provider model catalog.';
    END IF;

    SELECT count(*)
      INTO veo_omni_flash_count
      FROM public.todox_ai_provider_model m
      JOIN public.todox_ai_model_price p ON p.model_id = m.id
     WHERE lower(btrim(m.provider_code))='79ai'
       AND lower(btrim(m.provider_model_code))='veo_omni'
       AND m.enabled = true
       AND m.is_deprecated = false
       AND p.active = true
       AND lower(btrim(p.mode))='flash';

    SELECT count(*)
      INTO veo_31_fast_count
      FROM public.todox_ai_provider_model m
      JOIN public.todox_ai_model_price p ON p.model_id = m.id
     WHERE lower(btrim(m.provider_code))='79ai'
       AND lower(btrim(m.provider_model_code))='veo_3_1'
       AND m.enabled = true
       AND m.is_deprecated = false
       AND p.active = true
       AND lower(btrim(p.mode))='fast';

    SELECT count(*)
      INTO veo_31_lite_count
      FROM public.todox_ai_provider_model m
      JOIN public.todox_ai_model_price p ON p.model_id = m.id
     WHERE lower(btrim(m.provider_code))='79ai'
       AND lower(btrim(m.provider_model_code))='veo_3_1'
       AND m.enabled = true
       AND m.is_deprecated = false
       AND p.active = true
       AND lower(btrim(p.mode))='lite';

    SELECT count(*)
      INTO grok_normal_count
      FROM public.todox_ai_provider_model m
      JOIN public.todox_ai_model_price p ON p.model_id = m.id
     WHERE lower(btrim(m.provider_code))='79ai'
       AND lower(btrim(m.provider_model_code))='grok_video_heavy'
       AND m.enabled = true
       AND m.is_deprecated = false
       AND p.active = true
       AND lower(btrim(p.mode))='normal';

    -- The requested VEO/Grok business policy is reported separately. The repo audit
    -- proves model codes exist, but does not prove grok_video_heavy supports mode=normal.
    SELECT count(*)
      INTO requirement_count
      FROM public.todox_ai_provider_model
     WHERE lower(btrim(provider_code))='79ai'
       AND lower(provider_model_code) IN ('veo_omni', 'veo_3_1', 'grok_video_heavy')
       AND enabled = true
       AND is_deprecated = false;

    RAISE NOTICE 'RVIDEO_RUNTIME_VERIFY_PASS image_capability=% video_capability=% provider=79ai current_policy_model_count=% requested_policy_catalog_model_count=% veo_omni_flash=% veo_3_1_fast=% veo_3_1_lite=% grok_video_heavy_normal=% requested_policy_mode_contract=blocked',
        image_cap_id, video_cap_id, current_policy_count, requirement_count,
        veo_omni_flash_count, veo_31_fast_count, veo_31_lite_count, grok_normal_count;
END $$;

SELECT
    'RVIDEO_RUNTIME_VERIFY_PASS' AS status,
    'RVIDEO_VIDEO_MODEL_POLICY_BLOCKED' AS model_policy_status,
    '79ai' AS provider,
    'rvideo_scene_image_generation' AS image_capability,
    'rvideo_scene_video_generation' AS video_capability,
    4 AS current_policy_attempt_count,
    'pass' AS billing_schema_status,
    'pass' AS media_version_schema_status,
    'positive capability fallback + catalog pricing rules required' AS pricing_status,
    'catalog: veo_omni/flash, veo_3_1/fast, veo_3_1/lite; grok_video_heavy/normal remains blocked unless catalog evidence exists' AS model_catalog_audit,
    'requested VEO/Grok policy requires explicit grok_video_heavy mode=normal evidence' AS policy_note;
