# KIE Dance Sell Phase 2 Manual SQL

Run order:

1. `01_extend_dance_sell_jobs.sql`
2. `02_create_reference_versions.sql`
3. `03_seed_phase2_config.sql`
4. `04_verify_phase2.sql`

Notes:

- These scripts are manual only and are not executed by application startup.
- No secrets are stored here.
- Phase 2 keeps `unit_cost_points = 0` and render jobs use `point_status = not_required`; billing production is intentionally not enabled.
