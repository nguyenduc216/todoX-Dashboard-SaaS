# AI operation logs manual SQL

Manual scripts for Dance Sell AI operations, provider accounts, provider routes, operation assets, balance ledger, billing metadata and verification.

Run in order after reviewing against the target database:

1. `01_create_provider_accounts.sql`
2. `02_create_feature_provider_routes.sql`
3. `03_create_provider_operations.sql`
4. `04_create_operation_assets.sql`
5. `05_create_balance_ledger.sql`
6. `06_create_operation_billing.sql`
7. `07_extend_dance_sell_jobs.sql`
8. `08_seed_dance_sell_routes.sql`
9. `09_verify_ai_operation_logs.sql`
10. `10_harden_ai_operation_logs.sql`
11. `11_verify_runtime_contract.sql`

Notes:

- These scripts are idempotent and intended for manual execution only.
- They do not store API keys or secrets. `credential_config_name` stores environment-variable or secret-store reference names only.
- Default KIE routes are seeded disabled until an admin verifies credentials, pricing and provider contracts.
- `10_harden_ai_operation_logs.sql` adds runtime constraints and operation billing transaction audit rows.
- `11_verify_runtime_contract.sql` verifies the tables, columns, route seeds and hardening constraints expected by the application.
- Do not run against production without backup, review and maintenance window approval.
