-- Generated for manual review only. Do not execute automatically.
-- The generic DanceSell engine is the runtime for the fashion advertising dance video service.

UPDATE public.todox_ai_feature_provider_route
   SET is_default = false,
       enabled = false,
       updated_at = now()
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'reference_image'
   AND provider_code = 'local_composite'
   AND model_name = 'local_composite';

UPDATE public.todox_ai_feature_provider_route
   SET is_default = false,
       updated_at = now()
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'reference_image';

INSERT INTO public.todox_ai_feature_provider_route
    (feature_code, operation_type, provider_code, model_name, model_mode,
     route_priority, is_default, enabled, fallback_on, config_json)
SELECT 'dance_sell',
       'reference_image',
       '79ai',
       'seedream_5_0',
       'image',
       10,
       true,
       true,
       ARRAY[]::text[],
       '{"capability":"reference_image_generation","displayName":"79AI Seedream 5.0 Reference","submit_path":"/generateImage","poll_path":"/image","character_image_field":"base64Image","product_image_field":"image_2","project_id":"default","action_type":"create","editImage":"true","subjects":"[]","ratio":"9:16","mode":"vip","resolution":"2k"}'::jsonb
 WHERE NOT EXISTS (
     SELECT 1
       FROM public.todox_ai_feature_provider_route
      WHERE feature_code = 'dance_sell'
        AND operation_type = 'reference_image'
        AND provider_code = '79ai'
        AND model_name = 'seedream_5_0'
 );

UPDATE public.todox_ai_feature_provider_route
   SET route_priority = 10,
       is_default = true,
       enabled = true,
       model_mode = 'image',
       fallback_on = ARRAY[]::text[],
       config_json = COALESCE(config_json, '{}'::jsonb) || '{"capability":"reference_image_generation","displayName":"79AI Seedream 5.0 Reference","submit_path":"/generateImage","poll_path":"/image","character_image_field":"base64Image","product_image_field":"image_2","project_id":"default","action_type":"create","editImage":"true","subjects":"[]","ratio":"9:16","mode":"vip","resolution":"2k"}'::jsonb,
       updated_at = now()
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'reference_image'
   AND provider_code = '79ai'
   AND model_name = 'seedream_5_0';

UPDATE public.todox_ai_feature_provider_route
   SET is_default = false,
       updated_at = now()
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'motion_video';

INSERT INTO public.todox_ai_feature_provider_route
    (feature_code, operation_type, provider_code, model_name, model_mode,
     route_priority, is_default, enabled, fallback_on, config_json)
SELECT 'dance_sell',
       'motion_video',
       '79ai',
       'kling_video_motion',
       NULL,
       10,
       true,
       true,
       ARRAY[]::text[],
       '{"capability":"image_to_video","displayName":"Kling Motion Control","submit_path":"/create-video","poll_path":"/video","reference_image_field":"image","motion_video_field":"video"}'::jsonb
 WHERE NOT EXISTS (
     SELECT 1
       FROM public.todox_ai_feature_provider_route
      WHERE feature_code = 'dance_sell'
        AND operation_type = 'motion_video'
        AND provider_code = '79ai'
        AND model_name = 'kling_video_motion'
 );

UPDATE public.todox_ai_feature_provider_route
   SET route_priority = 10,
       is_default = true,
       enabled = true,
       fallback_on = ARRAY[]::text[],
       config_json = COALESCE(config_json, '{}'::jsonb) || '{"capability":"image_to_video","displayName":"Kling Motion Control","submit_path":"/create-video","poll_path":"/video","reference_image_field":"image","motion_video_field":"video"}'::jsonb,
       updated_at = now()
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'motion_video'
   AND provider_code = '79ai'
   AND model_name = 'kling_video_motion';

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

-- Reference generation now uses the production 79AI image route for seedream_5_0.
SELECT feature_code, operation_type, provider_code, model_name, model_mode, route_priority, is_default, enabled, fallback_on
  FROM public.todox_ai_feature_provider_route
 WHERE feature_code = 'dance_sell'
   AND operation_type IN ('reference_image', 'motion_video')
 ORDER BY operation_type, route_priority, provider_code, model_name;
