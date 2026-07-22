# Task 3 Precheck

- Branch: `refactor/ai-core-reset`
- Base commit: `df041b147e9ea2c9916b9eaaf5871099530fcf97`
- Working tree before Task 3: one pre-existing local dirty key file `TodoX.Web/keys/todox-vertex-sa.json`; not staged or committed.
- Database confirmed: `todo_saas`
- User: `postgres`
- PostgreSQL: 17.10

Verification scripts executed before code changes:
- `database/manual/ai-core-reset/30_verify_schema.sql`: 19/19 required tables existed.
- `database/manual/ai-core-reset/31_verify_provider_seed.sql`: 5 providers and KIE seed present.
- `database/manual/ai-core-reset/32_verify_runtime_contract.sql`: 6 removed legacy tables absent; required indexes and constraints present.

Precheck result: passed.
