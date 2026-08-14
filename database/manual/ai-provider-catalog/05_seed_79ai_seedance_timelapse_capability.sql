-- Manual, idempotent SQL. Review and execute against the target database separately.
-- Ensures the internal Timelapse route can resolve 79AI Seedance without changing global defaults.

BEGIN;

DO $$
DECLARE
    v_provider_id bigint;
BEGIN
    SELECT id
      INTO v_provider_id
      FROM public.todox_ai_provider
     WHERE provider_code = '79ai';

    IF v_provider_id IS NULL THEN
        RAISE EXCEPTION '79AI provider record was not found.';
    END IF;

    UPDATE public.todox_ai_provider_capability
       SET display_name = 'Seedance 2.0',
           endpoint_path = '/create-video',
           is_default = false,
           enabled = true,
           allow_user_select = false,
           config_json = COALESCE(config_json, '{}'::jsonb) || jsonb_build_object(
               'submit_path', '/create-video',
               'poll_path', '/video',
               'modes', jsonb_build_array('fast', 'fast_2', 'professional', 'professional_2'),
               'durations', to_jsonb(ARRAY[4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]),
               'resolutions', jsonb_build_array('480p', '720p', '1080p'),
               'status_at_catalog', 'ON',
               'runtime_owner', 'timelapse'
           ),
           updated_at = now()
     WHERE provider_id = v_provider_id
       AND capability_code = 'image_to_video'
       AND model_name = 'seedance_20_pro';

    IF NOT FOUND THEN
        INSERT INTO public.todox_ai_provider_capability
            (provider_id, provider_code, capability_code, display_name, model_name, endpoint_path,
             unit_type, unit_cost_points, is_default, enabled, allow_user_select,
             config_json, created_at, updated_at)
        VALUES
            (v_provider_id, '79ai', 'image_to_video', 'Seedance 2.0', 'seedance_20_pro', '/create-video',
             -- Required legacy routing fields only; provider pricing remains owned by the model price catalog.
             'request', 0, false, true, false,
             jsonb_build_object(
                 'submit_path', '/create-video',
                 'poll_path', '/video',
                 'modes', jsonb_build_array('fast', 'fast_2', 'professional', 'professional_2'),
                 'durations', to_jsonb(ARRAY[4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]),
                 'resolutions', jsonb_build_array('480p', '720p', '1080p'),
                 'status_at_catalog', 'ON',
                 'runtime_owner', 'timelapse'
             ),
             now(), now());
    END IF;
END $$;

COMMIT;
