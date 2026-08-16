-- Generated for manual review only. Do not execute automatically.
-- The generic DanceSell engine is the runtime for rDance Fashion.

UPDATE public.todox_ai_feature_provider_route
   SET is_default = false,
       enabled = false,
       updated_at = now()
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'reference_image';

INSERT INTO public.todox_ai_feature_provider_route
    (feature_code, operation_type, provider_code, provider_account_id, model_name,
     priority, is_default, enabled, allow_user_select, config_json)
SELECT 'dance_sell',
       'reference_image',
       'local_composite',
       NULL,
       'local_composite',
       10,
       true,
       true,
       false,
       '{"capability":"reference_image_generation","displayName":"Local reference composite"}'::jsonb
 WHERE NOT EXISTS (
     SELECT 1
       FROM public.todox_ai_feature_provider_route
      WHERE feature_code = 'dance_sell'
        AND operation_type = 'reference_image'
        AND provider_code = 'local_composite'
        AND model_name = 'local_composite'
 );

UPDATE public.todox_ai_feature_provider_route
   SET priority = 10,
       is_default = true,
       enabled = true,
       allow_user_select = false,
       updated_at = now()
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'motion_video'
   AND provider_code = '79ai'
   AND model_name = 'kling_video_motion';

UPDATE public.todox_ai_feature_provider_route
   SET is_default = false,
       updated_at = now()
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'motion_video';

INSERT INTO public.todox_ai_feature_provider_route
    (feature_code, operation_type, provider_code, provider_account_id, model_name,
     priority, is_default, enabled, allow_user_select, config_json)
SELECT 'dance_sell',
       'motion_video',
       '79ai',
       NULL,
       'kling_video_motion',
       10,
       true,
       true,
       false,
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
   SET priority = 100,
       is_default = false,
       enabled = true,
       allow_user_select = false,
       updated_at = now()
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'motion_video'
   AND provider_code = 'kie'
   AND model_name = 'kling-2.6/motion-control';

-- No verified 79AI image-edit model is present in the repository catalog audit.
-- Keep reference generation on the existing local composite path until one is configured.
SELECT feature_code, operation_type, provider_code, model_name, priority, is_default, enabled
  FROM public.todox_ai_feature_provider_route
 WHERE feature_code = 'dance_sell'
   AND operation_type IN ('reference_image', 'motion_video')
 ORDER BY operation_type, priority, provider_code, model_name;
