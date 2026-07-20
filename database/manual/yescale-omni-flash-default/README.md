# YEScale Omni Flash Default

Manual SQL only. Do not run through EF migrations.

Run order:

1. `00_preflight.sql`
2. Save the `rollback_statement` rows from preflight before applying.
3. `01_apply_yescale_omni_flash_default.sql`
4. `02_verify.sql`

Expected verify result:

- `enabled_default_count = 1`
- default row is `yescale_task_video / image_to_video / omni-flash`
- `mode = i2v(img_ref)`
- `max_prompt_characters = 4096`

Rollback:

- Run the saved `rollback_statement` rows from `00_preflight.sql`.
- The included `rollback_yescale_omni_flash_default.sql` intentionally stops unless replaced with the saved statements, because exact rollback needs the pre-apply live data.

Notes:

- Runtime credentials must remain outside source, SQL, logs, and publish artifacts.
- This package does not create migrations.
