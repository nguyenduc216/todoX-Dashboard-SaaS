# KIE Dance Sell Phase 1 SQL

Manual SQL only. Do not run automatically from application startup.

Run order:

1. `01_seed_kie_provider.sql`
2. `02_create_dance_sell_schema.sql`
3. `03_verify_kie_phase1.sql`

Notes:

- No API key is stored in SQL.
- Provider/capability are disabled/safe for production billing by default.
- Phase 1 does not deduct real points.
