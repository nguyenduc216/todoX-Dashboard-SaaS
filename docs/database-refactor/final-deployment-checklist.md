# Deployment Checklist

Before deployment:

- Confirm branch commit SHA and build artifact.
- Confirm database is backed up.
- Run SQL 39, 40, and 42 on target environment if not already applied.
- Verify `42_verify_task5_runtime.sql` passes.
- Confirm provider secrets exist only in environment/user-secrets/secret store.
- Confirm KIE/YEScale/OpenRouter/Vertex credentials are reference based.
- Run build/test/publish.
- Run non-paid smoke: provider account list, operation log list, billing dashboard, Dance Sell create/edit/upload validation.

After deployment:

- Watch render worker logs.
- Verify lease release/expiry counts.
- Verify no duplicated artifacts/usage/billing on callback/poll.
- Do not execute paid traffic without explicit approval.
