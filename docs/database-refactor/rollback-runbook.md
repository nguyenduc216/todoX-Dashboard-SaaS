# AI core reset rollback runbook

1. Stop app/background workers.
2. Create a post-failure backup if useful for diagnostics; do not commit it.
3. Restore the verified custom dump to a replacement database or restore over the test DB only after approval.
4. Validate with schema-only comparison and provider/model counts.
5. Restore environment variables/connection strings from secret store, not from committed files.
6. Re-run application build/tests before restarting workers.

## Verified backup

- Custom dump: `D:\todoX\db-backups\ai-core-reset\backup-before-ai-core-reset-20260722-1413.dump`
- SHA256: `124E5E3721A15B66E8A381D7B51F979B0FBD8F07154056AC6B3229AF3BA7D492`
