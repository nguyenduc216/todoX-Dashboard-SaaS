# TodoX Skill Endpoint deployment

Public endpoint: `https://skill.todox.vn`

## Architecture

`ChatGPT/Skill -> TodoX.SkillEndpoint -> TodoXAutomationAPI -> PostgreSQL / n8n / providers`

The Skill endpoint must not connect to PostgreSQL with arbitrary SQL. Read/repair/retry operations are exposed through controlled TodoX operations APIs.

## Existing TodoX runtime assumptions

The current TodoX foundation documentation defines:

- TodoX root: `E:\N8N.ANHDUC\todoX`
- TodoXAutomationAPI: `E:\N8N.ANHDUC\todoX\automation-api`
- Automation API local port: `8787`
- PostgreSQL: `127.0.0.1:5432`, database `todox`
- n8n runtime/UI port: `8997`
- Internal API authentication header: `X-TodoX-Secret`
- Database contract generator and Doctor V3.6 must be run after schema changes.

Do not commit API keys, passwords or production secrets to Git.

## Recommended IIS layout

Deploy `TodoX.SkillEndpoint` as a separate IIS site/application bound to `skill.todox.vn` with HTTPS.

Suggested publish directory:

`E:\N8N.ANHDUC\todoX\skill-endpoint\publish`

Suggested logs:

`E:\N8N.ANHDUC\todoX\logs\skill-endpoint`

## Required configuration

Use environment variables or protected IIS configuration:

- `SkillEndpoint__ApiKey` - independent key presented by the Skill client.
- `SkillEndpoint__TodoXOperationsBaseUrl=http://127.0.0.1:8787/`
- `SkillEndpoint__TodoXOperationsApiKey` - internal TodoX Automation API secret.
- `SkillEndpoint__AuditLogPath=E:\N8N.ANHDUC\todoX\logs\skill-endpoint\skill-audit.ndjson`

Do not reuse the public Skill key as the internal TodoX API secret.

## Database migration

After pulling branch `feature/todox-skill-endpoint`, open PowerShell as Administrator in the repository root.

Set the PostgreSQL password only in the current process, then execute:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
$env:PGPASSWORD = '<postgres password from server configuration>'
.\scripts\skill\Update-TodoXSkillDatabase.ps1
Remove-Item Env:PGPASSWORD
```

The script performs:

1. PostgreSQL connectivity check.
2. PostgreSQL backup to the documented TodoX backup directory.
3. Idempotent migration.
4. Validation of Skill/Ops tables.
5. Database Contract regeneration when the documented generator exists.
6. Doctor V3.6 when the documented script exists.
7. Transcript log under `artifacts\skill-migration`.

Migration file:

`database/migrations/20260807_001_todox_skill_ops.sql`

## Build and publish

```powershell
dotnet restore
dotnet build .\TodoX.SkillEndpoint\TodoX.SkillEndpoint.csproj -c Release
dotnet publish .\TodoX.SkillEndpoint\TodoX.SkillEndpoint.csproj -c Release -o E:\N8N.ANHDUC\todoX\skill-endpoint\publish
```

After IIS configuration, verify:

```powershell
Invoke-RestMethod https://skill.todox.vn/health
```

Expected service name: `todox-skill-endpoint` and status `ok`.

## Safety model

Read-only operations:

- inspect job
- inspect scene/task/provider state
- diagnostic
- repair-plan

Controlled mutation operations:

- reconcile
- retry failed scene(s)
- resume
- repair using whitelist actions

Every mutation requires `X-Idempotency-Key`. Repair also requires explicit confirmation. There is no arbitrary SQL endpoint.

## First production diagnostic

Use render Job 397 as the first diagnostic testcase after the Ops API is deployed. The expected diagnostic must distinguish local `TIMEOUT_PENDING` from provider state and must never submit a second provider task until reconciliation proves the existing task is terminal/absent.
