INSERT INTO public.todox_ai_provider_account
    (provider_code, account_name, environment, credential_config_name, currency, balance_unit, enabled, is_default, config_json)
SELECT 'kie', 'KIE production', 'production', 'KIE_API_KEY', 'USD', 'credits', false, true,
       '{"note":"Enable after verifying credentials and balance/pricing policy."}'::jsonb
WHERE NOT EXISTS (
    SELECT 1 FROM public.todox_ai_provider_account
     WHERE provider_code='kie' AND account_name='KIE production' AND environment='production'
);

UPDATE public.todox_ai_feature_provider_route
   SET is_default=false, updated_at=now()
 WHERE feature_code='dance_sell' AND operation_type='reference_image' AND is_default=true;

INSERT INTO public.todox_ai_feature_provider_route
    (feature_code, operation_type, provider_code, provider_account_id, model_name, priority, is_default, enabled, allow_user_select, config_json)
SELECT 'dance_sell', 'reference_image', 'kie', a.id, 'gpt-image-2-image-to-image', 100, true, false, true,
       '{"capability":"reference_image_generation","status":"disabled_until_contract_verified"}'::jsonb
  FROM public.todox_ai_provider_account a
 WHERE a.provider_code='kie' AND a.account_name='KIE production' AND a.environment='production'
   AND NOT EXISTS (
        SELECT 1 FROM public.todox_ai_feature_provider_route r
         WHERE r.feature_code='dance_sell' AND r.operation_type='reference_image'
           AND r.provider_code='kie' AND r.model_name='gpt-image-2-image-to-image'
           AND COALESCE(r.provider_account_id, '00000000-0000-0000-0000-000000000000'::uuid)=COALESCE(a.id, '00000000-0000-0000-0000-000000000000'::uuid)
   );

UPDATE public.todox_ai_feature_provider_route
   SET is_default=false, updated_at=now()
 WHERE feature_code='dance_sell' AND operation_type='motion_video' AND is_default=true;

INSERT INTO public.todox_ai_feature_provider_route
    (feature_code, operation_type, provider_code, provider_account_id, model_name, priority, is_default, enabled, allow_user_select, config_json)
SELECT 'dance_sell', 'motion_video', 'kie', a.id, 'kling-2.6/motion-control', 100, true, false, true,
       '{"capability":"motion_control_video","rateLimit":"20 generation requests / 10 seconds / account","maxConcurrentTasks":100}'::jsonb
  FROM public.todox_ai_provider_account a
 WHERE a.provider_code='kie' AND a.account_name='KIE production' AND a.environment='production'
   AND NOT EXISTS (
        SELECT 1 FROM public.todox_ai_feature_provider_route r
         WHERE r.feature_code='dance_sell' AND r.operation_type='motion_video'
           AND r.provider_code='kie' AND r.model_name='kling-2.6/motion-control'
           AND COALESCE(r.provider_account_id, '00000000-0000-0000-0000-000000000000'::uuid)=COALESCE(a.id, '00000000-0000-0000-0000-000000000000'::uuid)
   );
