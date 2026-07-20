BEGIN;

DO $$
DECLARE
    target_capability_id bigint;
    enabled_default_count integer;
    resolved_default_count integer;
BEGIN
    SELECT c.id
      INTO target_capability_id
      FROM public.todox_ai_provider p
      JOIN public.todox_ai_provider_capability c ON c.provider_id = p.id
     WHERE p.provider_code = 'yescale_task_video'
       AND p.enabled = true
       AND c.capability_code = 'image_to_video'
       AND c.model_name = 'omni-flash'
       AND c.enabled = true
     LIMIT 1;

    IF target_capability_id IS NULL THEN
        RAISE EXCEPTION 'Enabled YEScale omni-flash image_to_video capability was not found.';
    END IF;

    UPDATE public.todox_ai_provider_capability
       SET is_default = false,
           updated_by = 'manual_sql',
           updated_at = now()
     WHERE capability_code = 'image_to_video'
       AND is_default = true
       AND id <> target_capability_id;

    UPDATE public.todox_ai_provider_capability
       SET is_default = true,
           config_json = COALESCE(config_json, '{}'::jsonb) || '{
             "mode": "i2v(img_ref)",
             "max_prompt_characters": 4096,
             "aspect_ratios": ["16:9", "9:16"],
             "cost_source": "configured_tariff"
           }'::jsonb,
           updated_by = 'manual_sql',
           updated_at = now()
     WHERE id = target_capability_id;

    SELECT COUNT(*)
      INTO enabled_default_count
      FROM public.todox_ai_provider_capability
     WHERE capability_code = 'image_to_video'
       AND enabled = true
       AND is_default = true;

    IF enabled_default_count <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one enabled image_to_video default, found %.', enabled_default_count;
    END IF;

    SELECT COUNT(*)
      INTO resolved_default_count
      FROM public.todox_ai_provider p
      JOIN public.todox_ai_provider_capability c ON c.provider_id = p.id
     WHERE p.provider_code = 'yescale_task_video'
       AND p.enabled = true
       AND c.capability_code = 'image_to_video'
       AND c.model_name = 'omni-flash'
       AND c.enabled = true
       AND c.is_default = true
       AND c.config_json ->> 'mode' = 'i2v(img_ref)'
       AND c.config_json ->> 'max_prompt_characters' = '4096';

    IF resolved_default_count <> 1 THEN
        RAISE EXCEPTION 'YEScale omni-flash did not become the verified image_to_video default.';
    END IF;
END $$;

COMMIT;
