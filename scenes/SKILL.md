---
name: yescale-control-plane
description: Use when Codex or another code CLI is integrating, operating, or debugging YEScale AI Control Plane apps. Covers Default App compatibility, app access keys, metadata header instrumentation, request_id debugging, Control Plane MCP tools, traffic/access/policy boundaries, logs, billing ledger, task lifecycle, and production integration snippets for api.yescale.io.
---

# YEScale Control Plane

## Core Model

Treat YEScale as an AI Infra Control Plane, not only an AI gateway:

- Gateway calls AI through `https://api.yescale.io`.
- Control Plane operates AI production by App, Environment, Access, Traffic, Policy, Logs, Cost, Billing, Metadata, and Tasks.
- MCP at `https://yescale.io/mcp` uses a YEScale user access key to inspect or manage the same resources visible in the dashboard.

Do not confuse keys:

- **User access key**: authenticates MCP/dashboard management. Do not use this as a production API key.
- **App access key / API key**: authenticates requests to `api.yescale.io`. It belongs to an App and Environment so usage, logs, cost, policies, and metadata have scope.
- **Default App**: compatibility app for existing/legacy API keys and quick API keys from the API Keys page.

## Recommended Workflow

1. Identify or create the App.
2. Use `production`, `staging`, `development`, or a custom Environment.
3. Use an App access key for the request path. Existing API keys normally live in Default App.
4. Add metadata through `X-YEScale-Metadata`.
5. Log `x-request-id` from every response.
6. Debug with Control Plane logs, request detail, billing ledger, and task lifecycle.

## Metadata Contract

Free Plan accepts these 5 standard metadata fields:

- `end_user_id`: end user in the customer product, used for cost and abuse tracking.
- `tenant_id`: workspace, team, or tenant in the customer product.
- `feature`: feature inside the app, for example `agent_run`, `read_pdf`, `support_chat`.
- `plan`: end-user plan, for example `free`, `starter`, `pro`.
- `session_id`: chat, task, or workflow session.

Use short stable values. Do not put prompts, responses, secrets, tokens, or long text in metadata.

For exact examples, read `references/integration.md`.

## MCP Tool Use

When MCP is configured, prefer the live tools instead of guessing:

- `yescale_get_control_plane_guide`: Control Plane guide, metadata examples, request_id debugging.
- `yescale_list_apps`: find Default App, Playground, Canvas, and production apps.
- `yescale_get_integration_snippet`: get app/environment/access-aware integration snippets.
- `yescale_search_request_logs`: search by request_id, task_id, outcome, metadata.
- `yescale_get_request_detail`: inspect sanitized request detail.
- `yescale_search_billing_ledger`: trace charges/refunds by request_id, task_id, or session.
- `yescale_get_control_plane_breakdown`: analyze usage/cost by app, model, access, feature, tenant, or session.

If MCP is not configured, still follow the metadata and request_id contract when writing code.

## Production Guardrails

- Keep `x-request-id` in application logs.
- Use App-specific access keys instead of sharing one key across products.
- Use metadata consistently so dashboards can show cost by end user, tenant, feature, plan, and session.
- Access is the permission boundary. Traffic policy must not exceed the App access key's group, capability, model scope, or environment.
- Quota limits usage; Budget limits spend. Policy-blocked requests should be debugged in Control Plane logs.
- For media/task endpoints, debug by `task_id` as well as `request_id`; task failure/refund appears in task lifecycle and billing ledger.

## References

- Read `references/integration.md` when writing request code or explaining metadata.
- Read `references/mcp.md` when helping a user configure MCP in Codex, Claude, Cursor, VS Code, Gemini, or OpenCode.
