-- AI Studio shared Voice/Music catalog.
-- Idempotent script for manual execution. Codex did not execute this migration.

CREATE TABLE IF NOT EXISTS public.ai_studio_voices (
    id uuid PRIMARY KEY,
    name text NOT NULL,
    code text NOT NULL,
    provider_code text NOT NULL,
    provider_voice_id text NULL,
    compatibility_alias text NULL,
    gender text NULL,
    language_code text NULL,
    region text NULL,
    description text NULL,
    preview_file_name text NULL,
    preview_storage_key text NULL,
    preview_file_url text NULL,
    default_rate numeric(8,4) NOT NULL DEFAULT 1.0,
    min_rate numeric(8,4) NULL,
    max_rate numeric(8,4) NULL,
    is_active boolean NOT NULL DEFAULT true,
    is_default boolean NOT NULL DEFAULT false,
    sort_order integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by text NULL,
    updated_at timestamptz NULL,
    updated_by text NULL,
    CONSTRAINT ai_studio_voices_default_rate_positive CHECK (default_rate > 0),
    CONSTRAINT ai_studio_voices_min_rate_positive CHECK (min_rate IS NULL OR min_rate > 0),
    CONSTRAINT ai_studio_voices_max_rate_positive CHECK (max_rate IS NULL OR max_rate > 0),
    CONSTRAINT ai_studio_voices_rate_range CHECK (
        (min_rate IS NULL OR max_rate IS NULL OR min_rate <= max_rate)
        AND (min_rate IS NULL OR default_rate >= min_rate)
        AND (max_rate IS NULL OR default_rate <= max_rate)
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_studio_voices_code
    ON public.ai_studio_voices (lower(code));

CREATE INDEX IF NOT EXISTS ix_ai_studio_voices_active_sort
    ON public.ai_studio_voices (is_active, sort_order, lower(name));

CREATE INDEX IF NOT EXISTS ix_ai_studio_voices_provider
    ON public.ai_studio_voices (lower(provider_code), is_active);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_studio_voices_active_default
    ON public.ai_studio_voices (is_default)
    WHERE is_default = true AND is_active = true;

CREATE TABLE IF NOT EXISTS public.ai_studio_music (
    id uuid PRIMARY KEY,
    name text NOT NULL,
    code text NOT NULL,
    description text NULL,
    category text NOT NULL DEFAULT 'other',
    file_name text NULL,
    storage_key text NULL,
    file_url text NULL,
    duration_seconds integer NULL,
    mime_type text NULL,
    file_size bigint NULL,
    default_volume numeric(5,4) NOT NULL DEFAULT 0.8,
    loop_allowed boolean NOT NULL DEFAULT true,
    is_active boolean NOT NULL DEFAULT true,
    is_default boolean NOT NULL DEFAULT false,
    sort_order integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by text NULL,
    updated_at timestamptz NULL,
    updated_by text NULL,
    CONSTRAINT ai_studio_music_volume_range CHECK (default_volume >= 0 AND default_volume <= 1),
    CONSTRAINT ai_studio_music_duration_positive CHECK (duration_seconds IS NULL OR duration_seconds > 0),
    CONSTRAINT ai_studio_music_file_size_positive CHECK (file_size IS NULL OR file_size > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_studio_music_code
    ON public.ai_studio_music (lower(code));

CREATE INDEX IF NOT EXISTS ix_ai_studio_music_active_sort
    ON public.ai_studio_music (is_active, sort_order, lower(name));

CREATE INDEX IF NOT EXISTS ix_ai_studio_music_category
    ON public.ai_studio_music (lower(category), is_active);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_studio_music_active_default
    ON public.ai_studio_music (is_default)
    WHERE is_default = true AND is_active = true;

-- Seed only the compatibility voice whose provider_voice_id is not provider-specific.
-- a1/a2/a3 Vbee provider_voice_id values were not present in source/config, so they are intentionally not guessed here.
INSERT INTO public.ai_studio_voices
    (id, name, code, provider_code, provider_voice_id, compatibility_alias, gender, language_code, region, description,
     default_rate, min_rate, max_rate, is_active, is_default, sort_order, created_at, created_by, updated_at, updated_by)
VALUES
    ('00000000-0000-0000-0000-00000000a004', 'Custom', 'custom', 'custom', NULL, 'a4', NULL, 'vi-VN', NULL,
     'Compatibility alias a4. Provider-specific voice id is supplied by the caller in legacy flows.',
     1.0, 0.8, 1.2, true, true, 40, now(), 'migration', now(), 'migration')
ON CONFLICT (id) DO UPDATE SET
    name = EXCLUDED.name,
    provider_code = EXCLUDED.provider_code,
    compatibility_alias = EXCLUDED.compatibility_alias,
    description = EXCLUDED.description,
    default_rate = EXCLUDED.default_rate,
    min_rate = EXCLUDED.min_rate,
    max_rate = EXCLUDED.max_rate,
    is_active = EXCLUDED.is_active,
    is_default = EXCLUDED.is_default,
    sort_order = EXCLUDED.sort_order,
    updated_at = now(),
    updated_by = EXCLUDED.updated_by;

INSERT INTO public.ai_studio_music
    (id, name, code, description, category, default_volume, loop_allowed, is_active, is_default, sort_order, created_at, created_by, updated_at, updated_by)
VALUES
    ('00000000-0000-0000-0000-00000000b001', 'Default background music', 'default_music', 'Placeholder default catalog row. Upload the production MP3 before use.', 'other', 0.8, true, true, true, 10, now(), 'migration', now(), 'migration')
ON CONFLICT (id) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    category = EXCLUDED.category,
    default_volume = EXCLUDED.default_volume,
    loop_allowed = EXCLUDED.loop_allowed,
    is_active = EXCLUDED.is_active,
    is_default = EXCLUDED.is_default,
    sort_order = EXCLUDED.sort_order,
    updated_at = now(),
    updated_by = EXCLUDED.updated_by;
