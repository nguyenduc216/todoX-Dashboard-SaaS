# Phase 5.1 Rollback Checklist

1. Stop traffic/workers.
2. Restore previous binaries/config.
3. Use DB backup if SQL rollback is required.
4. Verify wallet/billing consistency.
5. Release or expire active leases.
6. Requeue or cancel active jobs according to business policy.
7. Restore traffic.
8. Monitor billing, usage, leases, and provider errors.

Do not manually edit wallet balances outside an audited rollback/reconciliation script.
