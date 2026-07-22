CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS public.todox_ai_operation_assets (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operation_id uuid NOT NULL REFERENCES dance_sell.dance_sell_provider_operations(id),
    asset_role text NOT NULL,
    media_id uuid NULL,
    object_key text NULL,
    public_url text NULL,
    provider_url text NULL,
    mime_type text NULL,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_operation_assets_unique_idx
    ON public.todox_ai_operation_assets(operation_id, asset_role, COALESCE(media_id, '00000000-0000-0000-0000-000000000000'::uuid), COALESCE(public_url, ''), COALESCE(provider_url, ''));

CREATE INDEX IF NOT EXISTS todox_ai_operation_assets_operation_idx ON public.todox_ai_operation_assets(operation_id, created_at);
CREATE INDEX IF NOT EXISTS todox_ai_operation_assets_role_idx ON public.todox_ai_operation_assets(asset_role);
