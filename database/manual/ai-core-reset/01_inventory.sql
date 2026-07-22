SET statement_timeout = '5min';
SET lock_timeout = '10s';

SELECT current_database() AS database_name,
       current_user AS connected_user,
       version() AS postgres_version,
       current_setting('search_path') AS search_path;

SELECT n.nspname AS schema_name,
       c.relname AS object_name,
       c.relkind AS object_kind,
       COALESCE(s.n_live_tup, 0) AS estimated_rows
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
LEFT JOIN pg_stat_user_tables s ON s.relid = c.oid
WHERE n.nspname IN ('public','dance_sell','billing','render','system')
  AND c.relkind IN ('r','v','m','S','f')
ORDER BY n.nspname, c.relname;
