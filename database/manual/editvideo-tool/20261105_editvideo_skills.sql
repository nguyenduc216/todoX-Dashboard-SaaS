-- TodoX.EditVideoTool: skill library managed in the dashboard. The customer's tool downloads every active skill
-- and installs it into Claude / Codex (~/.claude/skills/<slug>/SKILL.md).
-- Additive and idempotent; run through tools/prod-sql/Invoke-TodoXSql.ps1. No existing table is altered.

CREATE TABLE IF NOT EXISTS editvideo.skills (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id    uuid        NOT NULL REFERENCES system.tenants(id),
    slug         text        NOT NULL,              -- folder + skill name, always todox-...
    title        text        NOT NULL,              -- shown to people: "TodoX BĐS – vẽ bản đồ"
    description  text        NOT NULL,              -- tells Claude when to use the skill
    category     text        NULL,
    content      text        NOT NULL,              -- markdown body of SKILL.md (without front matter)
    is_active    boolean     NOT NULL DEFAULT true,
    version      integer     NOT NULL DEFAULT 1,
    sort_order   integer     NOT NULL DEFAULT 0,
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now(),
    updated_by   uuid        NULL,
    CONSTRAINT ck_editvideo_skills_slug CHECK (slug ~ '^todox-[a-z0-9]+(-[a-z0-9]+)*$' AND length(slug) <= 64)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_editvideo_skills_tenant_slug
    ON editvideo.skills(tenant_id, slug);
