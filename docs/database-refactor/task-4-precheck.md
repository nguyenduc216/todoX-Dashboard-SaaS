# Task 4 Precheck

- Branch: `refactor/ai-core-reset`
- Base commit: `76bf6a10f86534f846fac61eb009df406db1760a`
- Database verified: `todo_saas`
- PostgreSQL: 17.10
- Existing dirty local file excluded from work: `TodoX.Web/keys/todox-vertex-sa.json`

Verification scripts:

- `30_verify_schema.sql`: passed, 19/19 required tables exist.
- `31_verify_provider_seed.sql`: passed, 5 providers and KIE seed present.
- `32_verify_runtime_contract.sql`: passed, removed legacy tables absent.
- `34_verify_task3_runtime.sql`: passed, 11/11 checks, capability count = 36.
- `37_verify_task4_runtime.sql`: passed after Task 4 SQL fixes.

No destructive reset SQL was executed in Task 4.
