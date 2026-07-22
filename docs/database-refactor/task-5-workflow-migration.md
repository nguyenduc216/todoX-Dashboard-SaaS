# Task 5 Workflow Migration

Complete backend pieces:

- Generic operation log service queries render jobs/events/steps/inputs/artifacts, provider usage, billing, attempts, and wallet transactions.
- Provider diagnostics backend lists accounts, leases, health, cooldown, credential reference, usage, and cost without returning secret values.
- Provider balance ledger service records opening balance, top up, usage charge, refund, manual adjustment, and provider sync idempotently.
- Shared render completion service locks render jobs, prevents duplicate terminal completion, appends redacted events, upserts steps, creates artifacts idempotently, finalizes usage/billing, and releases leases.

Partial workflow migrations:

- Existing image/video workflows still need full provider-client adapter wiring before every provider submission can be declared fully migrated.
- Dance Sell generated reference path exists but needs live provider smoke and final provider adapter migration.

No paid traffic was executed.
