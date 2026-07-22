# AI core reset runbook

1. Confirm this is non-production/test database.
2. Stop web app workers: render worker, scene video worker, billing reconciliation worker, reup campaign worker.
3. Verify backup files from `00_backup_and_export.md` exist and `pg_restore --list` succeeds.
4. Review provider exports under `docs/database-refactor/`.
5. Run SQL package from `database/manual/ai-core-reset/` in README order.
6. Capture every command/result into `docs/database-refactor/sql-execution-log.csv`.
7. Run `30_verify_schema.sql`, `31_verify_provider_seed.sql`, and `32_verify_runtime_contract.sql`.
8. Do not restart production services from Codex; only report the publish/build output.

## Important

Task 1 prepared scripts only. Task 2 is the first task allowed to execute destructive reset SQL.
