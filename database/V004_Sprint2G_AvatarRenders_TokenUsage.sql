-- ============================================================
-- V004_Sprint2G_AvatarRenders_TokenUsage.sql
-- Additive migration for TodoX Dashboard (todo_saas).
-- Adds:
--   auth.user_avatar_renders   - per-image chibi render history
--   billing.token_usage_logs   - per-API-call token consumption ledger (for billing/deduction)
-- Does NOT alter any existing Foundation V2/V003 objects. Idempotent.
-- ============================================================

-- 1. Per-image avatar render log (one row per rendered image)
CREATE TABLE IF NOT EXISTS auth.user_avatar_renders (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     uuid REFERENCES system.tenants(id),
    user_id       uuid NOT NULL REFERENCES auth.app_users(id) ON DELETE CASCADE,
    generation_id uuid REFERENCES auth.user_chibi_generations(id) ON DELETE CASCADE,
    media_id      uuid REFERENCES media.media_files(id),
    image_url     text,
    prompt_input  text,               -- prompt the user typed / requested
    prompt_used   text,               -- prompt actually sent to the model (Gemini scenario or edited)
    model         varchar(100) DEFAULT 'imagen-3.0-generate-002',
    aspect        varchar(20)  DEFAULT '1:1',
    status        varchar(40)  NOT NULL DEFAULT 'completed',
    error_message text,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz
);

CREATE INDEX IF NOT EXISTS idx_avatar_renders_generation ON auth.user_avatar_renders(generation_id);
CREATE INDEX IF NOT EXISTS idx_avatar_renders_user ON auth.user_avatar_renders(user_id);

-- 2. Token usage ledger: one row per API call that consumes tokens (any provider)
CREATE TABLE IF NOT EXISTS billing.token_usage_logs (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id      uuid REFERENCES system.tenants(id),
    user_id        uuid REFERENCES auth.app_users(id),
    customer_id    uuid REFERENCES crm.customers(id),
    provider_code  varchar(80),        -- google-vertex-ai, woku, openrouter, gemini...
    model_code     varchar(120),
    operation      varchar(80) NOT NULL,   -- chibi_image, gemini_prompt, video_render...
    quantity       integer NOT NULL DEFAULT 1,   -- e.g. number of images
    unit           varchar(40) DEFAULT 'image',
    token_cost     numeric NOT NULL DEFAULT 0,    -- tokens deducted (converted)
    charged        boolean NOT NULL DEFAULT false, -- true if wallet was actually deducted (customer)
    reference_type varchar(80),
    reference_id   uuid,
    endpoint_code  varchar(120),
    status         varchar(40) NOT NULL DEFAULT 'success',
    metadata       jsonb DEFAULT '{}'::jsonb,
    created_at     timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_token_usage_customer ON billing.token_usage_logs(customer_id);
CREATE INDEX IF NOT EXISTS idx_token_usage_user ON billing.token_usage_logs(user_id);
CREATE INDEX IF NOT EXISTS idx_token_usage_created ON billing.token_usage_logs(created_at);

-- 3. Record migration version (ignore if already present)
INSERT INTO system.foundation_versions (version_code, script_name, status, message)
SELECT 'V004', 'V004_Sprint2G_AvatarRenders_TokenUsage.sql', 'success', 'Avatar renders + token usage ledger'
WHERE NOT EXISTS (SELECT 1 FROM system.foundation_versions WHERE version_code='V004');
