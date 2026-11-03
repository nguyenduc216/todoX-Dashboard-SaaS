# TodoX monorepo

Projects: `TodoX.Web` (dashboard), `TodoX.Landing`, `TodoX.EditVideoTool` (local MCP exe). Per-project rules live in
each project's `AGENTS.md` — read them before editing that project.

## Deploy

When the user says "deploy"/"deployed"/"đưa lên production", follow `deploy/README.md` exactly:
`deploy/Deploy-TodoXRemote.ps1 -Target auto -Plan` first, report the plan, then run it without `-Plan`.
Which database and which server folder come only from `deploy/targets.json`, `deploy/remote.local.json` and the
server's `C:/TodoXDeploy/deploy-config.json` — never guess them.

## Production SQL

Only through `tools/prod-sql/Invoke-TodoXSql.ps1` (dry run + mandatory `.test.sql`, then apply in one transaction).
Never run ad-hoc SQL against production.
