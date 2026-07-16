# Scene Media Versioning SQL

Standalone PostgreSQL scripts only. These are not EF migrations and must be run manually by an administrator after a database backup.

Recommended order:

1. `00_preflight_scene_media_versioning.sql`
2. `01_add_scene_media_versioning.sql`
3. `02_backfill_existing_scene_media.sql`
4. `03_seed_scene_media_versioning_settings.sql`
5. `04_mark_orphan_queued_versions.sql`
6. `verify_scene_media_versioning.sql`

Run each file with:

```bash
psql --set ON_ERROR_STOP=1 --file <script.sql>
```

Expected results:

- Versioning tables and pointer columns exist.
- Feature flags remain disabled by default.
- Existing image/video/final paths are backfilled only when source data exists.
- Orphan queued versions older than 30 minutes and without `render_job_id` are marked `failed`; no rows are deleted.
- Verify SQL returns readable result sets for table/column health, version counts, duplicate logical IDs, orphan queued versions, selected pointer problems, final item problems, and billing metadata gaps.

Rollback:

Run `rollback_scene_media_versioning.sql`. Rollback only disables feature flags. It does not delete version rows, media rows, billing records, jobs, or project events.
