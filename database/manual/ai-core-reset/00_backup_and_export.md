# Backup and export checklist

Task 1 verified backup:

- Custom dump: `D:\todoX\db-backups\ai-core-reset\backup-before-ai-core-reset-20260722-1413.dump`
- Custom dump SHA256: `124E5E3721A15B66E8A381D7B51F979B0FBD8F07154056AC6B3229AF3BA7D492`
- Schema-only backup: `D:\todoX\db-backups\ai-core-reset\backup-before-ai-core-reset-schema-20260722-1413.sql`
- Schema-only SHA256: `0A05FFB5BA531CC62DA665F253F56E47B97EC89ECE45435E0DB9F3283AB69687`
- `pg_restore --list` lines: `732`

Provider/model exports:

- `docs/database-refactor/provider-export.json`
- `docs/database-refactor/providers-before.csv`
- `docs/database-refactor/capabilities-before.csv`
- `docs/database-refactor/credential-references-before.csv`

Do not run destructive scripts until this backup can be restored in a separate database.
