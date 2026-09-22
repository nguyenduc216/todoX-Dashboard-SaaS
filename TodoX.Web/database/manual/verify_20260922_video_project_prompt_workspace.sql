SELECT table_schema, table_name, column_name, data_type
  FROM information_schema.columns
 WHERE (table_schema = 'settings'
        AND table_name = 'service_prompt_generations'
        AND column_name = 'video_project_id')
    OR (table_schema = 'video_render'
        AND table_name = 'video_projects'
        AND column_name = 'active_prompt_generation_id')
 ORDER BY table_schema, table_name, column_name;

SELECT schemaname, tablename, indexname, indexdef
  FROM pg_indexes
 WHERE schemaname = 'settings'
   AND tablename = 'service_prompt_generations'
   AND indexname = 'ix_service_prompt_generations_video_project';
