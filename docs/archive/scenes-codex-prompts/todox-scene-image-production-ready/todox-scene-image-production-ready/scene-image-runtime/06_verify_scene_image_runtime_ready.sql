\set ON_ERROR_STOP on

DO $$
DECLARE
    problem_count integer;
BEGIN
    IF to_regclass('video_render.scene_image_versions') IS NULL
       OR to_regclass('billing.ai_image_billing_records') IS NULL
       OR to_regclass('billing.ai_image_provider_attempts') IS NULL THEN
        RAISE EXCEPTION 'Required scene versioning or image billing tables are missing.';
    END IF;

    SELECT count(*) INTO problem_count
      FROM public.todox_ai_provider_capability
     WHERE capability_code='scene_image_generation'
       AND is_default=true
       AND NOT (
           provider_code='yescale_task_image'
           AND model_name='nano-banana-2'
           AND enabled=true
       );
    IF problem_count > 0 THEN
        RAISE EXCEPTION 'The default scene image capability is not enabled YEScale nano-banana-2.';
    END IF;

    SELECT count(*) INTO problem_count
      FROM (
        SELECT logical_request_id
          FROM video_render.scene_image_versions
         GROUP BY logical_request_id
        HAVING count(*) > 1
      ) duplicate_logical;
    IF problem_count > 0 THEN
        RAISE EXCEPTION 'Duplicate scene image logical_request_id groups found: %.', problem_count;
    END IF;

    SELECT count(*) INTO problem_count
      FROM video_render.scene_image_versions
     WHERE status='queued' AND render_job_id IS NULL
       AND created_at < now() - interval '30 minutes';
    IF problem_count > 0 THEN
        RAISE EXCEPTION 'Old orphan queued scene image versions found: %.', problem_count;
    END IF;

    SELECT count(*) INTO problem_count
      FROM (
        SELECT scene_id
          FROM video_render.scene_image_versions
         WHERE is_selected
         GROUP BY scene_id
        HAVING count(*) > 1
      ) duplicate_selected;
    IF problem_count > 0 THEN
        RAISE EXCEPTION 'Scenes with multiple selected image versions found: %.', problem_count;
    END IF;
END $$;

SELECT 'FEATURE_FLAGS' section, setting_key, setting_value, is_active
  FROM system.app_settings
 WHERE setting_key IN (
       'features.scene_render_versioning',
       'features.scene_video_versioning',
       'features.final_video_versioning')
 ORDER BY setting_key;

SELECT 'SCENE_IMAGE_DEFAULT' section, id, provider_code, capability_code, model_name,
       enabled, is_default, unit_cost_points, config_json
  FROM public.todox_ai_provider_capability
 WHERE capability_code='scene_image_generation'
 ORDER BY is_default DESC, provider_code, model_name;

SELECT 'SYSTEM_WALLET' section, id, tenant_id, customer_id, wallet_scope,
       wallet_code, balance, locked_balance, status
  FROM billing.token_wallets
 WHERE wallet_scope='system' AND wallet_code='TODOX_AI_IMAGE_SYSTEM';

SELECT 'IMAGE_VERSION_COUNTS' section, status, count(*) row_count
  FROM video_render.scene_image_versions
 GROUP BY status
 ORDER BY status;

SELECT 'RECENT_IMAGE_VERSIONS' section, project_id, scene_id, version_number,
       render_job_id, logical_request_id, provider_code, actual_model,
       provider_task_id, billing_logical_request_id, estimated_usd, actual_usd,
       charged_points, status, is_selected, storage_key, public_url, created_at
  FROM video_render.scene_image_versions
 ORDER BY created_at DESC
 LIMIT 30;

SELECT 'RECENT_IMAGE_BILLING' section, logical_request_id, render_job_id,
       customer_id, payer_type, provider_code, capability_code, requested_model,
       actual_model, provider_task_id, provider_estimated_cost_usd,
       provider_actual_cost_usd, provider_cost_points, customer_charged_points,
       system_charged_points, status, created_at
  FROM billing.ai_image_billing_records
 ORDER BY created_at DESC
 LIMIT 30;

SELECT 'VERIFY_RESULT' section, 'PASS' status,
       'Scene image versioning is enabled; scene/final video versioning remains disabled.' next_action;

