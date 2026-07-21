-- Verification only. Does not modify data.

SELECT column_name, data_type, is_nullable
  FROM information_schema.columns
 WHERE table_schema = 'dance_sell'
   AND table_name = 'dance_sell_jobs'
   AND column_name IN (
       'title',
       'character_media_id',
       'product_media_id',
       'motion_source_type',
       'motion_video_media_id',
       'placement_mode',
       'prepared_reference_media_id',
       'prepared_reference_status',
       'source_stage_status',
       'created_by',
       'updated_by'
   )
 ORDER BY column_name;

SELECT to_regclass('dance_sell.dance_sell_reference_versions') AS reference_versions_table;

SELECT conname, pg_get_constraintdef(oid) AS definition
  FROM pg_constraint
 WHERE conrelid IN (
       'dance_sell.dance_sell_jobs'::regclass,
       'dance_sell.dance_sell_reference_versions'::regclass
   )
   AND conname LIKE 'dance_sell_%'
 ORDER BY conname;
