# Task 5 Legacy Reference Check

Passed:

- Removed runtime tables are absent in database verification.
- New shared services use only render/provider usage/billing/account tables.
- Secret redaction is used in shared completion and diagnostics test credential responses.

Remaining blockers:

- `AiImageBillingService` still contains unique reserve/complete SQL and must be replaced by a thin adapter in the next hardening pass.
- Some provider clients still need full account-lease-first migration.

This report is intentionally explicit because these are production blockers, not cosmetic leftovers.
