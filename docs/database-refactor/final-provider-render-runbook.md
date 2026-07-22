# Provider Render Runbook

Normal submit flow:

1. Create or claim render job.
2. Validate and stage inputs.
3. Resolve provider/capability/model from database.
4. Claim provider account lease.
5. Resolve credential reference.
6. Reserve billing.
7. Submit provider task.
8. Poll or receive callback.
9. Call shared completion.
10. Stage artifact, finalize usage/billing, release lease.

Operations:

- Use operation logs to inspect render timeline, steps, attempts, artifacts, usage, billing, and wallet transactions.
- Use provider diagnostics to inspect account health, active leases, balance snapshot, and credential reference.
- Use balance ledger for manual provider balance adjustments and provider sync snapshots.

Never expose provider secrets in UI, logs, reports, or screenshots.
