# YEScale Video Provider SQL

Manual SQL only. Do not run through EF migrations.

Run order:

1. `00_preflight.sql`
2. `01_seed_yescale_video_provider.sql`
3. `02_verify_yescale_video_provider.sql`

Rollback:

- `rollback.sql`

Notes:

- Runtime secret must remain in `AiProviders__YEScale__AccessKey`.
- This seed does not mark any YEScale video row as default.
- Admin should choose the default provider/model later from the UI after staging verification.
