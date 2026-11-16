-- TodoX.EditVideoTool: AI music (Suno through 79AI) is tracked in editvideo.ai_tasks like images and videos.
-- Additive only: the kind check now also accepts 'music'. Run through tools/prod-sql/Invoke-TodoXSql.ps1.

ALTER TABLE editvideo.ai_tasks DROP CONSTRAINT IF EXISTS ck_editvideo_ai_tasks_kind;
ALTER TABLE editvideo.ai_tasks ADD CONSTRAINT ck_editvideo_ai_tasks_kind CHECK (kind IN ('image', 'video', 'music'));
