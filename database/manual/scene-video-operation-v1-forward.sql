-- Manual V1 schema + database writer fences. Does not modify historical job outcomes.
-- Deploy matching application code on every worker before enabling the UI.
BEGIN;
SET LOCAL lock_timeout='5s';
CREATE TABLE video_render.scene_video_operation_heads (
 tenant_id uuid NOT NULL,
 project_id bigint NOT NULL REFERENCES video_render.video_projects(id),
 scene_id bigint NOT NULL REFERENCES video_render.video_project_scenes(id),
 generation bigint NOT NULL CHECK(generation>=0),
 active_render_job_id uuid NOT NULL REFERENCES render.render_jobs(id),
 active_video_version_id uuid NOT NULL REFERENCES video_render.scene_video_versions(id),
 updated_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(tenant_id,scene_id),
 UNIQUE(tenant_id,project_id,scene_id)
);
CREATE TABLE video_render.scene_video_operation_transitions (
 id uuid PRIMARY KEY,
 tenant_id uuid NOT NULL,
 project_id bigint NOT NULL,
 scene_id bigint NOT NULL,
 old_generation bigint NOT NULL CHECK(old_generation>=0),
 proposed_generation bigint NOT NULL CHECK(proposed_generation=old_generation+1),
 old_job_id uuid NOT NULL REFERENCES render.render_jobs(id),
 old_version_id uuid NOT NULL REFERENCES video_render.scene_video_versions(id),
 old_logical_request_id text NOT NULL CHECK(length(old_logical_request_id)>0),
 new_job_id uuid NOT NULL UNIQUE REFERENCES render.render_jobs(id),
 new_version_id uuid NOT NULL UNIQUE REFERENCES video_render.scene_video_versions(id),
 new_logical_request_id text NOT NULL CHECK(length(new_logical_request_id)>0),
 actor_id uuid NOT NULL,
 idempotency_key uuid NOT NULL,
 request_hash text NOT NULL CHECK(length(request_hash)=64),
 risk_acknowledged boolean NOT NULL CHECK(risk_acknowledged),
 reason text NOT NULL CHECK(length(btrim(reason))>0),
 old_job_snapshot jsonb NOT NULL,
 old_version_snapshot jsonb NOT NULL,
 old_provider_outcome text NOT NULL DEFAULT 'UNKNOWN' CHECK(old_provider_outcome='UNKNOWN'),
 -- Activation is a head CAS plus job activation in one transaction.
 transition_state text NOT NULL DEFAULT 'PREPARED' CHECK(transition_state IN('PREPARED','ACTIVATED','ABANDONED')),
 created_at timestamptz NOT NULL DEFAULT now(),
 activated_at timestamptz,
 abandoned_at timestamptz,
 abandonment_reason text,
 FOREIGN KEY(tenant_id,project_id,scene_id) REFERENCES video_render.scene_video_operation_heads(tenant_id,project_id,scene_id),
 UNIQUE(tenant_id,scene_id,idempotency_key),
 CHECK(new_job_id<>old_job_id AND new_version_id<>old_version_id AND new_logical_request_id<>old_logical_request_id)
);
CREATE UNIQUE INDEX scene_video_operation_generation_unique ON video_render.scene_video_operation_transitions(tenant_id,scene_id,proposed_generation) WHERE transition_state IN('PREPARED','ACTIVATED');
COMMENT ON TABLE video_render.scene_video_operation_transitions IS
 'Durable generation transitions; UNKNOWN outcome retained; no provider cancellation.';

CREATE FUNCTION video_render.scene_video_job_is_superseded(t uuid,j uuid) RETURNS boolean
LANGUAGE sql STABLE AS $$
 SELECT EXISTS(SELECT 1 FROM video_render.scene_video_operation_transitions x
 JOIN video_render.scene_video_operation_heads h USING(tenant_id,project_id,scene_id)
 WHERE x.tenant_id=t AND x.old_job_id=j AND x.transition_state='ACTIVATED'
   AND x.activated_at IS NOT NULL AND h.generation>=x.proposed_generation);
$$;
CREATE FUNCTION video_render.scene_video_job_is_current(t uuid,j uuid) RETURNS boolean
LANGUAGE sql STABLE AS $$
 SELECT EXISTS(SELECT 1 FROM render.render_jobs r WHERE r.tenant_id=t AND r.id=j
  AND NOT EXISTS(SELECT 1 FROM video_render.scene_video_operation_heads h
    WHERE h.tenant_id=t AND (h.scene_id::text=COALESCE(r.input_json->>'sceneId',r.input_json->>'scene_id') OR (r.job_type='render_video_batch' AND h.generation>0 AND h.project_id::text=r.input_json->>'projectId' AND COALESCE(r.input_json->'sceneIds','[]'::jsonb) @> jsonb_build_array(h.scene_id)))
      AND (h.active_render_job_id<>j OR (h.generation>0 AND
           COALESCE(r.input_json->>'operationGeneration','')<>h.generation::text))));
$$;
CREATE FUNCTION video_render.assert_video_operation_current(t uuid,j uuid) RETURNS void
LANGUAGE plpgsql AS $$
DECLARE head record;
BEGIN
 FOR head IN SELECT h.active_render_job_id,h.generation,r.input_json FROM video_render.scene_video_operation_heads h JOIN render.render_jobs r
   ON r.tenant_id=h.tenant_id AND (h.scene_id::text=COALESCE(r.input_json->>'sceneId',r.input_json->>'scene_id') OR (r.job_type='render_video_batch' AND h.generation>0 AND h.project_id::text=r.input_json->>'projectId' AND COALESCE(r.input_json->'sceneIds','[]'::jsonb) @> jsonb_build_array(h.scene_id)))
   WHERE r.id=j AND r.tenant_id=t FOR SHARE OF h LOOP
  -- Inspect the locked tuple itself after a concurrent activation, not a pre-lock snapshot.
  IF head.active_render_job_id<>j OR (head.generation>0 AND COALESCE(head.input_json->>'operationGeneration','')<>head.generation::text) THEN
   RAISE EXCEPTION 'VIDEO_OPERATION_SUPERSEDED: old operation cannot submit or mutate current output' USING ERRCODE='55000';
  END IF;
 END LOOP;
 IF NOT EXISTS(SELECT 1 FROM render.render_jobs WHERE id=j AND tenant_id=t) THEN RAISE EXCEPTION 'VIDEO_OPERATION_OWNER_MISSING'; END IF;
END $$;
CREATE FUNCTION video_render.fence_video_operation_write() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE owner_job uuid; context_job uuid; t uuid; project bigint; snapshot jsonb;
BEGIN
 t:=NEW.tenant_id;
 context_job:=NULLIF(current_setting('todox.video_job',true),'')::uuid;
 IF TG_TABLE_NAME='scene_video_versions' THEN
  PERFORM h.scene_id FROM video_render.scene_video_operation_heads h WHERE h.tenant_id=t AND h.scene_id=NEW.scene_id FOR SHARE;
  owner_job:=OLD.render_job_id;
  IF owner_job IS NOT NULL AND video_render.scene_video_job_is_superseded(t,owner_job) THEN
   IF context_job IS NOT NULL AND OLD.is_selected AND NOT NEW.is_selected
      AND (to_jsonb(NEW)-'is_selected')=(to_jsonb(OLD)-'is_selected') THEN
    PERFORM video_render.assert_video_operation_current(t,context_job);
    RETURN NEW; -- Current operation may deselect a historical completed output after its own completion.
   END IF;
   IF current_setting('todox.video_selection',true)='true' AND OLD.status='completed'
      AND (to_jsonb(NEW)-ARRAY['is_selected','selected_at','selected_by','updated_at'])=
          (to_jsonb(OLD)-ARRAY['is_selected','selected_at','selected_by','updated_at']) THEN RETURN NEW; END IF;
   RAISE EXCEPTION 'VIDEO_OPERATION_SUPERSEDED: historical version is immutable to workers' USING ERRCODE='55000';
  END IF;
 END IF;
 IF TG_TABLE_NAME='render_jobs' THEN
  IF TG_OP='UPDATE' THEN
   IF NEW.job_type='render_scene_video' AND OLD.status='draft' AND NEW.status='cancelled'
     AND OLD.provider_task_id IS NULL AND OLD.provider_video_id_base IS NULL
     AND NOT EXISTS(SELECT 1 FROM render.scene_video_submit_intents i WHERE i.tenant_id=t AND i.render_job_id=NEW.id)
     AND EXISTS(SELECT 1 FROM video_render.scene_video_operation_transitions x WHERE x.tenant_id=t AND x.new_job_id=NEW.id AND x.transition_state='PREPARED') THEN
    RETURN NEW; -- Only an unclaimable, never-submitted preparation can be abandoned; outcome of old operation is untouched.
   END IF;
   IF NEW.job_type='render_scene_video' THEN
    PERFORM video_render.assert_video_operation_current(t,NEW.id);
   END IF;
   IF NEW.job_type='render_scene_video' AND video_render.scene_video_job_is_superseded(t,NEW.id) THEN
    RAISE EXCEPTION 'VIDEO_OPERATION_SUPERSEDED: historical job cannot be retried/activated' USING ERRCODE='55000';
   END IF;
  END IF;
 END IF;
 IF context_job IS NOT NULL THEN
  PERFORM video_render.assert_video_operation_current(t,context_job);
  IF TG_TABLE_NAME='video_project_scenes' THEN
   IF EXISTS(SELECT 1 FROM video_render.scene_video_operation_heads h WHERE h.tenant_id=t AND h.scene_id=NEW.id AND h.generation>0)
     AND NOT EXISTS(SELECT 1 FROM render.render_jobs j WHERE j.id=context_job AND j.tenant_id=t
       AND j.input_json->>'sceneId'=NEW.id::text AND j.input_json->>'projectId'=NEW.project_id::text) THEN
    RAISE EXCEPTION 'VIDEO_OPERATION_SCOPE_MISMATCH';
   END IF;
  END IF;
 END IF;
 -- Merges carry the exact selected-version and head-generation snapshot they consumed.
 IF TG_TABLE_NAME IN('video_projects','final_video_versions') THEN
  snapshot:=NULLIF(current_setting('todox.video_merge',true),'')::jsonb;
  IF TG_TABLE_NAME='video_projects' AND NEW.status='completed' AND EXISTS(
    SELECT 1 FROM video_render.scene_video_operation_heads h JOIN render.render_jobs j ON j.id=h.active_render_job_id
    WHERE h.tenant_id=t AND h.project_id=NEW.id AND j.status<>'completed') THEN
    RAISE EXCEPTION 'VIDEO_PROJECT_GENERATION_NOT_COMPLETED' USING ERRCODE='55000';
  END IF;
  IF snapshot IS NOT NULL THEN
   IF TG_TABLE_NAME='video_projects' THEN project:=NEW.id; ELSE project:=NEW.project_id; END IF;
   PERFORM h.scene_id FROM video_render.scene_video_operation_heads h WHERE h.tenant_id=t AND h.project_id=project FOR SHARE;
   IF EXISTS(SELECT 1 FROM video_render.scene_video_operation_heads h WHERE h.tenant_id=t AND h.project_id=project
      AND COALESCE((snapshot->'generations'->>h.scene_id::text)::bigint,0)<>h.generation)
    OR EXISTS(SELECT 1 FROM video_render.video_project_scenes s WHERE s.tenant_id=t AND s.project_id=project
      AND COALESCE(snapshot->'selected'->>s.id::text,'')<>COALESCE(s.selected_video_version_id::text,'')) THEN
     RAISE EXCEPTION 'VIDEO_MERGE_SNAPSHOT_STALE' USING ERRCODE='55000';
   END IF;
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER scene_video_version_generation_fence BEFORE UPDATE ON video_render.scene_video_versions
 FOR EACH ROW EXECUTE FUNCTION video_render.fence_video_operation_write();
CREATE TRIGGER scene_video_job_generation_fence BEFORE INSERT OR UPDATE ON render.render_jobs
 FOR EACH ROW EXECUTE FUNCTION video_render.fence_video_operation_write();
CREATE TRIGGER scene_video_scene_generation_fence BEFORE UPDATE ON video_render.video_project_scenes
 FOR EACH ROW EXECUTE FUNCTION video_render.fence_video_operation_write();
CREATE TRIGGER scene_video_project_generation_fence BEFORE UPDATE ON video_render.video_projects
 FOR EACH ROW EXECUTE FUNCTION video_render.fence_video_operation_write();
CREATE TRIGGER scene_video_final_generation_fence BEFORE UPDATE ON video_render.final_video_versions
 FOR EACH ROW EXECUTE FUNCTION video_render.fence_video_operation_write();
COMMIT;
