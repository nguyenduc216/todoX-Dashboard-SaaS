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

COMMIT;
