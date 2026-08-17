-- Generated for manual review only. Do not execute automatically.
-- Switch DanceSell reference generation only after the 79AI subjects payload
-- has been verified against the live provider contract.

UPDATE public.todox_ai_feature_provider_route
   SET is_default = false,
       enabled = false,
       updated_at = now()
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'reference_image'
   AND provider_code = '79ai'
   AND model_name = 'seedream_5_0';

UPDATE public.todox_ai_feature_provider_route
   SET route_priority = 10,
       is_default = true,
       enabled = true,
       model_mode = 'image',
       fallback_on = ARRAY[]::text[],
       config_json = COALESCE(config_json, '{}'::jsonb) || jsonb_build_object(
           'capability', 'reference_image_generation',
           'displayName', '79AI GPT Image 2 Reference',
           'submit_path', '/generateImage',
           'poll_path', '/image',
           'character_image_field', 'base64Image',
           'subject_schema', 'json_stringified_array_of_image_data_uris',
           'project_id', 'default',
           'action_type', 'create',
           'editImage', 'true',
           'ratio', '9:16',
           'mode', 'medium',
           'resolution', '2k'
       ),
       updated_at = now()
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'reference_image'
   AND provider_code = '79ai'
   AND model_name = 'imagegen_2_0';

INSERT INTO public.todox_ai_feature_provider_route
    (feature_code, operation_type, provider_code, model_name, model_mode,
     route_priority, is_default, enabled, fallback_on, config_json)
SELECT 'dance_sell',
       'reference_image',
       '79ai',
       'imagegen_2_0',
       'image',
       10,
       true,
       true,
       ARRAY[]::text[],
       jsonb_build_object(
           'capability', 'reference_image_generation',
           'displayName', '79AI GPT Image 2 Reference',
           'submit_path', '/generateImage',
           'poll_path', '/image',
           'character_image_field', 'base64Image',
           'subject_schema', 'json_stringified_array_of_image_data_uris',
           'project_id', 'default',
           'action_type', 'create',
           'editImage', 'true',
           'ratio', '9:16',
           'mode', 'medium',
           'resolution', '2k'
       )
 WHERE NOT EXISTS (
     SELECT 1
       FROM public.todox_ai_feature_provider_route
      WHERE feature_code = 'dance_sell'
        AND operation_type = 'reference_image'
        AND provider_code = '79ai'
        AND model_name = 'imagegen_2_0'
 );

UPDATE public.todox_ai_feature_provider_route
   SET is_default = false,
       updated_at = now()
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'reference_image'
   AND NOT (
       provider_code = '79ai'
       AND model_name = 'imagegen_2_0'
   );

SELECT feature_code, operation_type, provider_code, model_name, model_mode,
       route_priority, is_default, enabled, fallback_on, config_json
  FROM public.todox_ai_feature_provider_route
 WHERE feature_code = 'dance_sell'
   AND operation_type = 'reference_image'
 ORDER BY route_priority, provider_code, model_name;
