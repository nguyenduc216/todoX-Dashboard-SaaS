-- TodoX.EditVideoTool: the customer's EditVideo projects and the videos made in them, shown in "Jobs của tôi".
-- Project/video information is sent by the app automatically; the video file itself only when the person presses
-- "Đồng bộ" (stored with the other job videos under customer-videos, not in the 1 GB EditVideo storage).
-- Additive and idempotent. Run through tools/prod-sql/Invoke-TodoXSql.ps1.

CREATE TABLE IF NOT EXISTS editvideo.projects (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES system.tenants(id),
    customer_id uuid NOT NULL REFERENCES crm.customers(id) ON DELETE CASCADE,
    license_id uuid NULL REFERENCES editvideo.licenses(id) ON DELETE SET NULL,
    project_key text NOT NULL,
    project_name text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, customer_id, project_key)
);

CREATE INDEX IF NOT EXISTS editvideo_projects_customer_idx ON editvideo.projects (customer_id, updated_at DESC);

CREATE TABLE IF NOT EXISTS editvideo.project_videos (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES system.tenants(id),
    customer_id uuid NOT NULL REFERENCES crm.customers(id) ON DELETE CASCADE,
    project_id uuid NOT NULL REFERENCES editvideo.projects(id) ON DELETE CASCADE,
    license_id uuid NULL REFERENCES editvideo.licenses(id) ON DELETE SET NULL,
    device_id uuid NULL REFERENCES editvideo.devices(id) ON DELETE SET NULL,
    client_video_id text NOT NULL,          -- the app's own id for the exported file
    title text NOT NULL,
    file_name text NOT NULL,
    duration_seconds numeric(10,2) NULL,
    aspect text NULL,
    style text NULL,
    skill text NULL,
    skill_version int NULL,
    size_bytes bigint NULL,
    rendered_at timestamptz NULL,
    sync_status text NOT NULL DEFAULT 'local' CHECK (sync_status IN ('local', 'uploading', 'synced')),
    video_url text NULL,
    uploaded_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, customer_id, client_video_id)
);

CREATE INDEX IF NOT EXISTS editvideo_project_videos_project_idx ON editvideo.project_videos (project_id, rendered_at DESC);
