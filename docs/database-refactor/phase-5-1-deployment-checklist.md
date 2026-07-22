# Phase 5.1 Deployment Checklist

1. Backup DB.
2. Confirm commit SHA.
3. Pause workers.
4. Apply SQL in order: `43`, `44`, `45`.
5. Verify config references without printing secret values.
6. Publish artifact from `artifacts\publish\phase5-1-prod-hardening`.
7. Backup IIS site binaries/config.
8. Stage deployment.
9. Run health checks.
10. Run non-paid smoke.
11. Monitor leases, errors, billing, usage, and reconciliation.
12. Switch traffic only after manual approval.
13. Run post-deploy verification.

Current checklist status: blocked until SQL and DB integration tests pass.
