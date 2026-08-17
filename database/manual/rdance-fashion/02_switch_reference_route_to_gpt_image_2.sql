-- Generated for manual review only. Do not execute automatically.
-- Switch DanceSell reference generation to the verified 79AI try-on payload.

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
           'subject_schema', 'form_subject_url_fields',
           'domain', '79ai.net',
           'project_id', 'default',
           'action_type', 'create',
           'sync', 'false',
           'ratio', '16:9',
           'category', 'FASHION',
           'mode', 'low',
           'resolution', '1k',
           'num_outputs', '1',
           'language', 'VI'
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
           'subject_schema', 'form_subject_url_fields',
           'domain', '79ai.net',
           'project_id', 'default',
           'action_type', 'create',
           'sync', 'false',
           'ratio', '16:9',
           'category', 'FASHION',
           'mode', 'low',
           'resolution', '1k',
           'num_outputs', '1',
           'language', 'VI'
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
