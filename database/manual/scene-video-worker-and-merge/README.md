# Scene Video Worker And Merge SQL

This package is manual SQL only.

Purpose:

- verify that scene/final versioning schema required by the scene-video worker already exists
- re-assert indexes/constraints used by selected-version and merge flows
- provide rollout checks before enabling YEScale video in staging/production

Run order:

1. `00_preflight.sql`
2. `01_upgrade_scene_video_jobs.sql`
3. `02_upgrade_scene_video_versions.sql`
4. `03_upgrade_final_video_versions.sql`
5. `04_seed_yescale_video_provider.sql`
6. `05_add_constraints_and_indexes.sql`
7. `06_backfill.sql`
8. `07_verify.sql`

Rollback:

- `rollback.sql`

Notes:

- This package intentionally does not create EF migrations.
- It also does not execute any destructive schema rollback for existing versioning tables.
