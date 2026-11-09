-- Every V2 draft that shows an uploaded reference image must now use it in the render settings.
DO $$
DECLARE
    remaining int;
    repaired int;
BEGIN
    SELECT count(*) INTO remaining
      FROM video_render.rvideo_job_settings s
      JOIN video_render.video_projects p ON p.id = s.project_id AND p.tenant_id = s.tenant_id
      JOIN render.render_jobs j ON j.id = p.core_job_id AND j.tenant_id = p.tenant_id
     WHERE j.logical_request_id LIKE 'rvideo-v2-new:%'
       AND (s.skip_character OR s.character_mode = 'NONE')
       AND NULLIF(btrim(p.source_image_url), '') IS NOT NULL;
    IF remaining > 0 THEN
        RAISE EXCEPTION 'V2 drafts still ignore their uploaded reference: %', remaining;
    END IF;

    SELECT count(*) INTO repaired
      FROM video_render.rvideo_job_settings s
      JOIN video_render.video_projects p ON p.id = s.project_id AND p.tenant_id = s.tenant_id
      JOIN render.render_jobs j ON j.id = p.core_job_id AND j.tenant_id = p.tenant_id
     WHERE j.logical_request_id LIKE 'rvideo-v2-new:%'
       AND s.character_mode = 'UPLOAD'
       AND NOT s.skip_character
       AND s.character_snapshot_json->>'fileUrl' = btrim(p.source_image_url);
    RAISE NOTICE 'V2 drafts using their uploaded reference: %', repaired;
END $$;
