CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS reup;

CREATE TABLE IF NOT EXISTS reup.campaigns (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    customer_id uuid NOT NULL,
    created_by_user_id uuid NULL,
    name text NOT NULL,
    description text NULL,
    caption text NULL,
    hashtags text NULL,
    status text NOT NULL DEFAULT 'draft',
    total_tasks int NOT NULL DEFAULT 0,
    pending_tasks int NOT NULL DEFAULT 0,
    running_tasks int NOT NULL DEFAULT 0,
    completed_tasks int NOT NULL DEFAULT 0,
    failed_tasks int NOT NULL DEFAULT 0,
    cancelled_tasks int NOT NULL DEFAULT 0,
    duplicate_warning_count int NOT NULL DEFAULT 0,
    started_at timestamptz NULL,
    completed_at timestamptz NULL,
    stopped_at timestamptz NULL,
    stop_requested boolean NOT NULL DEFAULT false,
    error_code text NULL,
    error_message text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT reup_campaigns_status_check CHECK (status IN ('draft', 'ready', 'running', 'stopping', 'stopped', 'completed', 'failed'))
);

CREATE INDEX IF NOT EXISTS ix_reup_campaigns_customer_created ON reup.campaigns(customer_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_reup_campaigns_tenant_customer ON reup.campaigns(tenant_id, customer_id);
CREATE INDEX IF NOT EXISTS ix_reup_campaigns_status ON reup.campaigns(status);

CREATE TABLE IF NOT EXISTS reup.campaign_videos (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    campaign_id uuid NOT NULL REFERENCES reup.campaigns(id) ON DELETE CASCADE,
    reference_video_id uuid NOT NULL REFERENCES content.reference_videos(id),
    order_index int NOT NULL DEFAULT 0,
    selected boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_reup_campaign_videos UNIQUE(campaign_id, reference_video_id)
);

CREATE INDEX IF NOT EXISTS ix_reup_campaign_videos_campaign ON reup.campaign_videos(campaign_id, order_index);
CREATE INDEX IF NOT EXISTS ix_reup_campaign_videos_reference ON reup.campaign_videos(reference_video_id);

CREATE TABLE IF NOT EXISTS reup.campaign_pages (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    campaign_id uuid NOT NULL REFERENCES reup.campaigns(id) ON DELETE CASCADE,
    social_page_id uuid NOT NULL REFERENCES social.customer_pages(id),
    selected boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_reup_campaign_pages UNIQUE(campaign_id, social_page_id)
);

CREATE INDEX IF NOT EXISTS ix_reup_campaign_pages_campaign ON reup.campaign_pages(campaign_id);
CREATE INDEX IF NOT EXISTS ix_reup_campaign_pages_page ON reup.campaign_pages(social_page_id);

CREATE TABLE IF NOT EXISTS reup.video_assets (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    customer_id uuid NOT NULL,
    reference_video_id uuid NOT NULL REFERENCES content.reference_videos(id),
    source_url text NOT NULL,
    platform text NOT NULL DEFAULT 'tiktok',
    provider text NOT NULL DEFAULT 'tikwm',
    resolved_video_url text NULL,
    local_path text NULL,
    file_name text NULL,
    file_size_bytes bigint NULL,
    content_type text NULL,
    status text NOT NULL DEFAULT 'created',
    error_code text NULL,
    error_message text NULL,
    expires_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT reup_video_assets_status_check CHECK (status IN ('created', 'resolving', 'ready', 'failed', 'expired'))
);

CREATE INDEX IF NOT EXISTS ix_reup_video_assets_ref ON reup.video_assets(customer_id, reference_video_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_reup_video_assets_tenant_customer ON reup.video_assets(tenant_id, customer_id);
CREATE INDEX IF NOT EXISTS ix_reup_video_assets_status ON reup.video_assets(status);

CREATE TABLE IF NOT EXISTS reup.publish_tasks (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    campaign_id uuid NOT NULL REFERENCES reup.campaigns(id) ON DELETE CASCADE,
    customer_id uuid NOT NULL,
    reference_video_id uuid NOT NULL REFERENCES content.reference_videos(id),
    social_page_id uuid NOT NULL REFERENCES social.customer_pages(id),
    page_access_token_id uuid NULL REFERENCES social.page_access_tokens(id),
    video_asset_id uuid NULL REFERENCES reup.video_assets(id) ON DELETE SET NULL,
    status text NOT NULL DEFAULT 'pending',
    duplicate_warning boolean NOT NULL DEFAULT false,
    previous_success_task_id uuid NULL REFERENCES reup.publish_tasks(id),
    caption_used text NULL,
    hashtags_used text NULL,
    facebook_video_id text NULL,
    facebook_post_url text NULL,
    facebook_raw_response jsonb NULL,
    token_check_status text NULL,
    token_check_error text NULL,
    error_code text NULL,
    error_message text NULL,
    attempt_count int NOT NULL DEFAULT 0,
    max_attempts int NOT NULL DEFAULT 2,
    locked_by text NULL,
    locked_at timestamptz NULL,
    started_at timestamptz NULL,
    completed_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT reup_publish_tasks_status_check CHECK (status IN ('pending', 'checking_page', 'resolving_video', 'publishing', 'completed', 'failed', 'cancelled', 'skipped'))
);

CREATE INDEX IF NOT EXISTS ix_reup_publish_tasks_campaign ON reup.publish_tasks(campaign_id, created_at);
CREATE INDEX IF NOT EXISTS ix_reup_publish_tasks_pick ON reup.publish_tasks(status, locked_at, created_at);
CREATE INDEX IF NOT EXISTS ix_reup_publish_tasks_page_status ON reup.publish_tasks(social_page_id, status);
CREATE INDEX IF NOT EXISTS ix_reup_publish_tasks_duplicate_lookup ON reup.publish_tasks(customer_id, reference_video_id, social_page_id, status);
CREATE INDEX IF NOT EXISTS ix_reup_publish_tasks_tenant_customer ON reup.publish_tasks(tenant_id, customer_id);
CREATE INDEX IF NOT EXISTS ix_reup_publish_tasks_token ON reup.publish_tasks(page_access_token_id);

CREATE TABLE IF NOT EXISTS reup.publish_logs (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NULL,
    campaign_id uuid NULL REFERENCES reup.campaigns(id) ON DELETE CASCADE,
    task_id uuid NULL REFERENCES reup.publish_tasks(id) ON DELETE CASCADE,
    level text NOT NULL DEFAULT 'info',
    step text NOT NULL,
    message text NOT NULL,
    data jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT reup_publish_logs_level_check CHECK (level IN ('debug', 'info', 'warning', 'error'))
);

CREATE INDEX IF NOT EXISTS ix_reup_publish_logs_campaign_created ON reup.publish_logs(campaign_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_reup_publish_logs_task_created ON reup.publish_logs(task_id, created_at DESC);

CREATE OR REPLACE FUNCTION reup.set_updated_at()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_reup_campaigns_set_updated_at ON reup.campaigns;
CREATE TRIGGER trg_reup_campaigns_set_updated_at BEFORE UPDATE ON reup.campaigns FOR EACH ROW EXECUTE FUNCTION reup.set_updated_at();

DROP TRIGGER IF EXISTS trg_reup_video_assets_set_updated_at ON reup.video_assets;
CREATE TRIGGER trg_reup_video_assets_set_updated_at BEFORE UPDATE ON reup.video_assets FOR EACH ROW EXECUTE FUNCTION reup.set_updated_at();

DROP TRIGGER IF EXISTS trg_reup_publish_tasks_set_updated_at ON reup.publish_tasks;
CREATE TRIGGER trg_reup_publish_tasks_set_updated_at BEFORE UPDATE ON reup.publish_tasks FOR EACH ROW EXECUTE FUNCTION reup.set_updated_at();

INSERT INTO auth.permissions (id, module, action, name, description, permission_group, is_active, created_at)
SELECT gen_random_uuid(), v.module, v.action, v.name, v.description, 'Reup Campaign', true, now()
FROM (VALUES
    ('reup_campaigns', 'view', 'Xem chiến dịch reup', 'Cho phép xem danh sách và kết quả chiến dịch reup video.'),
    ('reup_campaigns', 'create', 'Tạo chiến dịch reup', 'Cho phép tạo chiến dịch reup video.'),
    ('reup_campaigns', 'update', 'Sửa chiến dịch reup', 'Cho phép cập nhật chiến dịch reup video.'),
    ('reup_campaigns', 'delete', 'Xóa chiến dịch reup', 'Cho phép xóa chiến dịch reup video.'),
    ('reup_campaigns', 'run', 'Chạy chiến dịch reup', 'Cho phép chạy chiến dịch reup video.'),
    ('reup_campaigns', 'stop', 'Dừng chiến dịch reup', 'Cho phép dừng chiến dịch reup video.'),
    ('reup_campaigns', 'retry', 'Retry task reup', 'Cho phép chạy lại task reup video bị lỗi.')
) AS v(module, action, name, description)
WHERE NOT EXISTS (
    SELECT 1 FROM auth.permissions p WHERE p.module = v.module AND p.action = v.action
);

INSERT INTO auth.role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM auth.roles r
JOIN auth.permissions p ON p.module = 'reup_campaigns'
WHERE r.code IN ('administrator_root', 'root', 'admin', 'administrator', 'customer', 'customer_owner', 'customer_admin')
  AND NOT EXISTS (
      SELECT 1 FROM auth.role_permissions rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );
