-- RVideo V2 drafts (created by /jobs/rvideo/new, logical_request_id 'rvideo-v2-new:...') whose uploaded
-- reference image was saved only to video_projects.source_image_url: before fix 272b617 the V2 update did not
-- write video_render.rvideo_job_settings, so the render pipeline still saw "no character" and scene images
-- ignored the reference. Point the settings at the uploaded image (UPLOAD mode), as the V2 upload intended.
-- Data only, idempotent: rows already in UPLOAD/LIBRARY mode are untouched.
WITH targets AS (
    SELECT s.project_id,
           s.tenant_id,
           btrim(p.source_image_url) AS file_url,
           (SELECT mf.object_key
              FROM media.media_files mf
             WHERE mf.public_url = btrim(p.source_image_url) OR mf.file_url = btrim(p.source_image_url)
             ORDER BY mf.created_at DESC
             LIMIT 1) AS object_key
      FROM video_render.rvideo_job_settings s
      JOIN video_render.video_projects p
        ON p.id = s.project_id AND p.tenant_id = s.tenant_id
      JOIN render.render_jobs j
        ON j.id = p.core_job_id AND j.tenant_id = p.tenant_id
     WHERE j.logical_request_id LIKE 'rvideo-v2-new:%'
       AND (s.skip_character OR s.character_mode = 'NONE')
       AND NULLIF(btrim(p.source_image_url), '') IS NOT NULL
)
UPDATE video_render.rvideo_job_settings s
   SET skip_character = false,
       character_mode = 'UPLOAD',
       selected_character_id = NULL,
       character_snapshot_json = jsonb_build_object(
           'source', 'UPLOAD',
           'fileName', 'Ảnh tham chiếu',
           'storageKey', COALESCE(t.object_key, ''),
           'fileUrl', t.file_url),
       updated_at = now()
  FROM targets t
 WHERE s.project_id = t.project_id AND s.tenant_id = t.tenant_id;
