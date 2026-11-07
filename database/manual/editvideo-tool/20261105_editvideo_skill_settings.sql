-- TodoX.EditVideoTool: fixed settings per skill (aspect, length, style, caption look, transition) that the server
-- applies when the skill runs, so they never ship to the customer's machine. Stored on the draft and frozen with
-- each published version. Additive and idempotent; run through tools/prod-sql/Invoke-TodoXSql.ps1.
-- Adding a column with a default does not fire the immutability trigger on editvideo.skill_versions.

ALTER TABLE editvideo.skills ADD COLUMN IF NOT EXISTS settings jsonb NOT NULL DEFAULT '{}'::jsonb;
ALTER TABLE editvideo.skill_versions ADD COLUMN IF NOT EXISTS settings jsonb NOT NULL DEFAULT '{}'::jsonb;
