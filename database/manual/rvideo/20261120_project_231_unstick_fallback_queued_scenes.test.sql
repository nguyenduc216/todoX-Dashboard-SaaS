DO $$
DECLARE n int;
BEGIN
  SELECT count(*) INTO n FROM video_render.video_project_scenes WHERE project_id = 231 AND status = 'failed';
  IF n <> 7 THEN RAISE EXCEPTION 'expected 7 failed scenes in project 231, got %', n; END IF;
  SELECT count(*) INTO n FROM video_render.video_project_scenes WHERE project_id = 231 AND status IN ('video_queued', 'video_rendering');
  IF n <> 0 THEN RAISE EXCEPTION '% scenes of project 231 still shown rendering', n; END IF;
END $$;
