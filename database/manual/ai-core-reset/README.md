# AI core reset SQL package

This package is prepared by Task 1 only. Review every script before Task 2 execution.

Run order:

1. `01_inventory.sql`
2. `02_clear_test_data.sql`
3. `03_drop_legacy_tables.sql`
4. `04_normalize_provider.sql`
5. `05_create_provider_accounts.sql`
6. `06_create_provider_account_credentials.sql`
7. `09_rebuild_render_core.sql`
8. `07_create_provider_account_leases.sql`
9. `08_create_provider_balance_ledger.sql`
10. `10_rebuild_provider_usage.sql`
11. `11_rebuild_ai_billing.sql`
12. `12_rebuild_dance_sell.sql`
13. `13_indexes_constraints.sql`
14. `20_seed_preserved_providers.sql`
15. `21_seed_provider_accounts.sql`
16. `22_seed_dance_sell_models.sql`
17. `30_verify_schema.sql`
18. `31_verify_provider_seed.sql`
19. `32_verify_runtime_contract.sql`

Never commit database dumps or credential secret values. This package stores only schema and metadata/reference names.
