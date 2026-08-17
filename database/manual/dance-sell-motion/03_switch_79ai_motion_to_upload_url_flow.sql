-- Manual production route switch for DanceSell fashion motion video.
-- Do not execute automatically. Review against production provider catalog/pricing first.

BEGIN;

UPDATE public.todox_ai_feature_provider_route
   SET is_default = false,
       enabled = false,
       updated_at = now()
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'motion_video'
   AND provider_code = '79ai'
   AND model_name = 'kling_video_motion';

UPDATE public.todox_ai_feature_provider_route
   SET is_default = false,
       updated_at = now()
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'motion_video'
   AND provider_code = '79ai';

INSERT INTO public.todox_ai_feature_provider_route
    (feature_code, operation_type, provider_code, model_name, model_mode,
     route_priority, enabled, is_default, fallback_on, config_json, created_at, updated_at)
SELECT 'dance_sell',
       'motion_video',
       '79ai',
       'kling_video_motion_3',
       'motion',
       10,
       true,
       true,
       ARRAY[]::text[],
       '{
          "capability":"image_to_video",
          "displayName":"Kling 3.0 Motion Control",
          "base_url":"https://v2.api.gommo.net",
          "domain":"79ai.net",
          "project_id":"default",
          "upload_image_path":"/ai/upload/image",
          "upload_video_path":"/ai/upload/video",
          "motion_submit_path":"/ai/jobs/video/kling_video_motion_3",
          "poll_path":"/ai/jobs/{task_id}?media=video",
          "poll_id_field":"id_base",
          "upload_image_field":"file",
          "upload_video_field":"video_file",
          "mode":"standard",
          "ratio":"default",
          "subType":"motion",
          "background_source":"input_video",
          "include_images_zero_url":"true"
        }'::jsonb,
       now(),
       now()
 WHERE NOT EXISTS (
     SELECT 1
       FROM public.todox_ai_feature_provider_route
      WHERE feature_code = 'dance_sell'
        AND operation_type = 'motion_video'
        AND provider_code = '79ai'
        AND model_name = 'kling_video_motion_3'
 );

UPDATE public.todox_ai_feature_provider_route
   SET route_priority = 10,
       enabled = true,
       is_default = true,
       model_mode = 'motion',
       fallback_on = ARRAY[]::text[],
       config_json = COALESCE(config_json, '{}'::jsonb) || '{
          "capability":"image_to_video",
          "displayName":"Kling 3.0 Motion Control",
          "base_url":"https://v2.api.gommo.net",
          "domain":"79ai.net",
          "project_id":"default",
          "upload_image_path":"/ai/upload/image",
          "upload_video_path":"/ai/upload/video",
          "motion_submit_path":"/ai/jobs/video/kling_video_motion_3",
          "poll_path":"/ai/jobs/{task_id}?media=video",
          "poll_id_field":"id_base",
          "upload_image_field":"file",
          "upload_video_field":"video_file",
          "mode":"standard",
          "ratio":"default",
          "subType":"motion",
          "background_source":"input_video",
          "include_images_zero_url":"true"
        }'::jsonb,
       updated_at = now()
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'motion_video'
   AND provider_code = '79ai'
   AND model_name = 'kling_video_motion_3';

UPDATE public.todox_ai_feature_provider_route
   SET route_priority = 100,
       is_default = false,
       enabled = true,
       fallback_on = ARRAY['provider_error','timeout']::text[],
       updated_at = now()
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'motion_video'
   AND provider_code = 'kie'
   AND model_name = 'kling-2.6/motion-control';

SELECT feature_code, operation_type, provider_code, model_name, model_mode,
       route_priority, enabled, is_default, config_json
  FROM public.todox_ai_feature_provider_route
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'motion_video'
 ORDER BY route_priority, provider_code, model_name;

COMMIT;
