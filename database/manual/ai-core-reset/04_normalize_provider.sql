BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));

ALTER TABLE public.todox_ai_provider
    ADD COLUMN IF NOT EXISTS environment text NOT NULL DEFAULT 'production',
    ADD COLUMN IF NOT EXISTS runtime_config_json jsonb NOT NULL DEFAULT '{}'::jsonb;

ALTER TABLE public.todox_ai_provider_capability
    ADD COLUMN IF NOT EXISTS operation_type text NULL,
    ADD COLUMN IF NOT EXISTS feature_codes text[] NOT NULL DEFAULT ARRAY[]::text[],
    ADD COLUMN IF NOT EXISTS endpoint_path text NULL,
    ADD COLUMN IF NOT EXISTS runtime_config_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS pricing_unit text NULL,
    ADD COLUMN IF NOT EXISTS provider_code text NULL;

UPDATE public.todox_ai_provider_capability c
SET provider_code = p.provider_code
FROM public.todox_ai_provider p
WHERE p.id = c.provider_id
  AND c.provider_code IS NULL;

UPDATE public.todox_ai_provider_capability
SET operation_type = COALESCE(
        operation_type,
        config_json->>'operation_type',
        CASE
            WHEN capability_code IN ('text_to_video','image_to_video','motion_control_video','dance_sell_motion_video') THEN 'video'
            WHEN capability_code LIKE '%image%' OR capability_code LIKE '%avatar%' OR capability_code LIKE '%character%' OR capability_code LIKE '%poster%' OR capability_code LIKE '%thumbnail%' THEN 'image'
            ELSE capability_code
        END
    ),
    feature_codes = CASE
        WHEN feature_codes <> ARRAY[]::text[] THEN feature_codes
        WHEN config_json ? 'feature_code' THEN ARRAY[config_json->>'feature_code']
        ELSE ARRAY[capability_code]
    END,
    runtime_config_json = CASE
        WHEN runtime_config_json <> '{}'::jsonb THEN runtime_config_json
        ELSE COALESCE(config_json, '{}'::jsonb)
    END,
    pricing_unit = COALESCE(pricing_unit, unit_type);

COMMIT;
