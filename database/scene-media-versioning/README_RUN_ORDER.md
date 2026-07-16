# Scene Media Versioning - SQL Run Order

These SQL files are standalone patches, not EF migrations. Do not run them from Codex. Run them manually after backup and after reviewing the target database.

Recommended order:

1. Back up the database.
2. Run `00_preflight_scene_media_versioning.sql`.
3. Run `01_add_scene_media_versioning.sql`.
4. Run `02_backfill_existing_scene_media.sql`.
5. Run `03_seed_scene_media_versioning_settings.sql`.
6. Run `verify_scene_media_versioning.sql`.
7. Build/test/publish the application.
8. Enable feature flags only after smoke testing.

Feature flags are seeded as `false`:

- `features.scene_render_versioning`
- `features.scene_video_versioning`
- `features.final_video_versioning`

Rollback:

- Run `rollback_scene_media_versioning.sql`.
- Rollback only disables feature flags.
- It does not delete version rows, media rows, billing history, or render job history.

Run every SQL file with `psql --set ON_ERROR_STOP=1`.
