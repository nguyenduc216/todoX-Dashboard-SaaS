-- TodoX.EditVideoTool: published versions of each skill. A version is a frozen copy of the skill at publish time;
-- it is never edited, so customers can keep using (and go back to) the version they like.
-- Additive and idempotent; run through tools/prod-sql/Invoke-TodoXSql.ps1. No existing table is altered.

CREATE TABLE IF NOT EXISTS editvideo.skill_versions (
    id            uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     uuid        NOT NULL REFERENCES system.tenants(id),
    skill_id      uuid        NOT NULL REFERENCES editvideo.skills(id) ON DELETE CASCADE,
    version_no    integer     NOT NULL CHECK (version_no > 0),
    version_name  text        NOT NULL,          -- "Bản tối giản", "Thêm bản đồ" ...
    title         text        NOT NULL,          -- snapshot of the skill at publish time
    description   text        NOT NULL,
    content       text        NOT NULL,
    notes         text        NULL,              -- what changed
    created_at    timestamptz NOT NULL DEFAULT now(),
    created_by    uuid        NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_editvideo_skill_versions_skill_no
    ON editvideo.skill_versions(skill_id, version_no);

-- Versions are append-only: an UPDATE is always refused (DELETE only happens when the whole skill is deleted).
CREATE OR REPLACE FUNCTION editvideo.skill_versions_refuse_update() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'editvideo.skill_versions rows are immutable; publish a new version instead'
        USING ERRCODE = 'restrict_violation';
END $$;

DROP TRIGGER IF EXISTS trg_editvideo_skill_versions_immutable ON editvideo.skill_versions;
CREATE TRIGGER trg_editvideo_skill_versions_immutable
    BEFORE UPDATE ON editvideo.skill_versions
    FOR EACH ROW EXECUTE FUNCTION editvideo.skill_versions_refuse_update();
