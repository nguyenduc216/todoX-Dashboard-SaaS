\set ON_ERROR_STOP on
-- Single read-only query. Use an existing read-only role/service and -qAt for JSON evidence.
-- Required operator inputs: project_id, tenant_id, job_uuid. Never substitutes scene versions.
SELECT jsonb_build_object(
 'projectId',p.id,'tenantId',p.tenant_id,'coreJobId',p.core_job_id,'projectStatus',p.status,
 'previousFinalVideoUrl',p.final_video_url,'previousFinalVideoPath',p.final_video_path,
 'selectedFinalVideoVersionId',p.selected_final_video_version_id,'configuredTotalSeconds',p.total_seconds,
 'items',COALESCE((
   SELECT jsonb_agg(jsonb_build_object(
     'sceneId',s.id,'itemOrder',s.scene_index,'sceneProjectId',s.project_id,'sceneTenantId',s.tenant_id,
     'sceneVideoVersionId',s.selected_video_version_id,'versionSceneId',v.scene_id,
     'versionProjectId',v.project_id,'versionTenantId',v.tenant_id,
     'status',v.status,'isSelected',v.is_selected,'durationSeconds',s.duration_seconds,
     'lineageValid',COALESCE(v.project_id=s.project_id AND v.tenant_id=s.tenant_id AND v.scene_id=s.id,false),
     'videoPath',v.source_file_path,'storageKey',v.storage_key,'resultMediaId',v.result_media_id,'publicUrl',v.public_url
   ) ORDER BY s.scene_index)
   FROM video_render.video_project_scenes s
   LEFT JOIN video_render.scene_video_versions v ON v.id=s.selected_video_version_id
   WHERE s.project_id=p.id AND s.tenant_id=p.tenant_id
 ),'[]'::jsonb)
) AS selected_scene_evidence
FROM video_render.video_projects p
WHERE p.id=:'project_id'::bigint AND p.tenant_id=:'tenant_id'::uuid AND p.core_job_id=:'job_uuid'::uuid;
