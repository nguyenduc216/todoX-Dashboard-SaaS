# Rollback Checklist

Code rollback:

- Revert to previous deployed commit.
- Do not restore removed legacy tables.
- Stop workers before rollback if active jobs are being processed.

Database rollback:

- SQL 39 and 40 are additive/index-only. Prefer leaving them in place.
- If rollback is required, use the pre-task database backup, not ad hoc deletes.
- Validate no active leases remain stuck; expire stale leases if needed.

Post-rollback:

- Run `30_verify_schema.sql`, `32_verify_runtime_contract.sql`, and the last known valid runtime verification.
- Confirm no provider secrets were logged or committed.
