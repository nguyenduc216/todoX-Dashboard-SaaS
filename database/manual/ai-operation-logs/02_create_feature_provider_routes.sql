CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS public.todox_ai_feature_provider_route (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    feature_code text NOT NULL,
    operation_type text NOT NULL,
    provider_code text NOT NULL,
    provider_capability_id uuid NULL,
    provider_account_id uuid NULL REFERENCES public.todox_ai_provider_account(id),
    model_name text NOT NULL,
    priority integer NOT NULL DEFAULT 100,
    is_default boolean NOT NULL DEFAULT false,
    enabled boolean NOT NULL DEFAULT false,
    allow_user_select boolean NOT NULL DEFAULT false,
    config_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_feature_provider_route_unique_idx
    ON public.todox_ai_feature_provider_route(feature_code, operation_type, provider_code, model_name, COALESCE(provider_account_id, '00000000-0000-0000-0000-000000000000'::uuid));

CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_feature_provider_route_one_default_idx
    ON public.todox_ai_feature_provider_route(feature_code, operation_type)
    WHERE is_default = true;

CREATE INDEX IF NOT EXISTS todox_ai_feature_provider_route_enabled_idx
    ON public.todox_ai_feature_provider_route(feature_code, operation_type, enabled, priority);

CREATE INDEX IF NOT EXISTS todox_ai_feature_provider_route_model_idx
    ON public.todox_ai_feature_provider_route(provider_code, model_name);
