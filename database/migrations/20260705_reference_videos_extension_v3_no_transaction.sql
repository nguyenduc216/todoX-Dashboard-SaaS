-- TodoX reference videos + Chrome Extension collector
-- V3 no global transaction. Do not insert into auth.permissions.code because it is a generated column.

CREATE SCHEMA IF NOT EXISTS content;

CREATE TABLE IF NOT EXISTS content.reference_videos (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id uuid NOT NULL,
    created_by_user_id uuid NULL,
    platform text NOT NULL DEFAULT 'unknown',
    source_url text NOT NULL,
    normalized_url text NULL,
    external_video_id text NULL,
    channel_name text NULL,
    channel_url text NULL,
    author_handle text NULL,
    title text NULL,
    description text NULL,
    hashtags text[] NOT NULL DEFAULT ARRAY[]::text[],
    published_at timestamptz NULL,
    thumbnail_url text NULL,
    raw_metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    status text NOT NULL DEFAULT 'new',
    reup_job_id uuid NULL,
    added_from text NOT NULL DEFAULT 'chrome_extension',
    is_deleted boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT reference_videos_platform_chk CHECK (platform IN ('tiktok', 'youtube', 'facebook', 'instagram', 'unknown')),
    CONSTRAINT reference_videos_status_chk CHECK (status IN ('new', 'queued', 'processing', 'completed', 'failed', 'ignored'))
);

CREATE INDEX IF NOT EXISTS ix_reference_videos_customer_created
    ON content.reference_videos (customer_id, created_at DESC)
    WHERE is_deleted = false;

CREATE INDEX IF NOT EXISTS ix_reference_videos_customer_platform
    ON content.reference_videos (customer_id, platform)
    WHERE is_deleted = false;

CREATE UNIQUE INDEX IF NOT EXISTS ux_reference_videos_active_source
    ON content.reference_videos (customer_id, platform, source_url)
    WHERE is_deleted = false;

CREATE TABLE IF NOT EXISTS content.extension_tokens (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id uuid NOT NULL,
    user_id uuid NOT NULL,
    token_hash text NOT NULL,
    token_prefix text NOT NULL,
    name text NULL,
    is_active boolean NOT NULL DEFAULT true,
    last_used_at timestamptz NULL,
    expires_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    revoked_at timestamptz NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_extension_tokens_hash
    ON content.extension_tokens (token_hash);

CREATE INDEX IF NOT EXISTS ix_extension_tokens_customer_user
    ON content.extension_tokens (customer_id, user_id, created_at DESC);

INSERT INTO auth.permissions (id, module, action, name, description, is_active, created_at)
SELECT gen_random_uuid(), 'reference_videos', 'view',
       'Xem link video tham chiếu',
       'Cho phép xem danh sách link video tham chiếu của khách hàng.',
       true, now()
WHERE NOT EXISTS (
    SELECT 1 FROM auth.permissions WHERE module = 'reference_videos' AND action = 'view'
);

INSERT INTO auth.permissions (id, module, action, name, description, is_active, created_at)
SELECT gen_random_uuid(), 'reference_videos', 'create',
       'Thêm link video tham chiếu',
       'Cho phép thêm link video tham chiếu thủ công hoặc từ extension.',
       true, now()
WHERE NOT EXISTS (
    SELECT 1 FROM auth.permissions WHERE module = 'reference_videos' AND action = 'create'
);

INSERT INTO auth.permissions (id, module, action, name, description, is_active, created_at)
SELECT gen_random_uuid(), 'reference_videos', 'delete',
       'Xóa link video tham chiếu',
       'Cho phép xóa mềm link video tham chiếu của khách hàng.',
       true, now()
WHERE NOT EXISTS (
    SELECT 1 FROM auth.permissions WHERE module = 'reference_videos' AND action = 'delete'
);

INSERT INTO auth.permissions (id, module, action, name, description, is_active, created_at)
SELECT gen_random_uuid(), 'reference_videos', 'export',
       'Xuất link video tham chiếu',
       'Cho phép xuất danh sách link video tham chiếu.',
       true, now()
WHERE NOT EXISTS (
    SELECT 1 FROM auth.permissions WHERE module = 'reference_videos' AND action = 'export'
);

INSERT INTO auth.permissions (id, module, action, name, description, is_active, created_at)
SELECT gen_random_uuid(), 'extension', 'download',
       'Tải Chrome Extension',
       'Cho phép phát hành token và tải Chrome Extension TodoX Video Collector.',
       true, now()
WHERE NOT EXISTS (
    SELECT 1 FROM auth.permissions WHERE module = 'extension' AND action = 'download'
);

INSERT INTO auth.role_permissions (role_id, permission_id)
SELECT r.id, p.id
  FROM auth.roles r
  JOIN auth.permissions p
    ON (
        (p.module = 'reference_videos' AND p.action IN ('view', 'create', 'delete', 'export'))
        OR (p.module = 'extension' AND p.action = 'download')
    )
 WHERE r.role_type = 'customer'
    OR r.code IN ('administrator_root', 'admin', 'customer', 'customer_owner')
ON CONFLICT DO NOTHING;
