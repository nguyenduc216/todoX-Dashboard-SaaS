# Codex Task 01 — Core Billing Facade + Job Lifecycle Hardening

## Branch
Work only on:

`feature/core-api-platform`

Do not modify or merge `main`.

## Context
TodoX is being refactored into a shared Core Platform so Dashboard, Zalo Mini App, Telegram, Partner API, and future clients all call the same business/application layer.

Current Core files already exist under:

`TodoX.Web/Services/Platform/`

Key existing pieces:

- `CorePlatformContracts.cs`
- `CoreExecutionRouter.cs`
- `CoreServiceCatalogService.cs`
- `CoreServiceJobHandler.cs`
- `CoreJobApplicationService.cs`
- `CoreApiCallerResolver.cs`
- `CoreApiEndpointExtensions.cs`
- `CorePlatformServiceCollectionExtensions.cs`

Canonical job table is:

`render.render_jobs`

Canonical service catalog is:

`catalog.services`

Do NOT create parallel tables such as `api_jobs`, `zalo_jobs`, `partner_jobs`, or another universal job table.

## Critical architectural rules

1. Dashboard, Zalo, Telegram, Partner and API must share one Core application layer.
2. Transport-specific auth must remain outside business logic.
3. Core business logic must consume `CoreRequestContext` only.
4. Existing Timelapse/RVideo/RDance execution code must NOT be rewritten in this task.
5. Do not connect Timelapse yet.
6. Do not modify production DB or run migrations.
7. If DB schema changes are truly needed, create standalone SQL under:
   `database/manual/core-api-platform/`
   and do not execute it.
8. Preserve backward compatibility with current Dashboard code.
9. Public API remains feature-flagged via `CoreApi:Enabled`.
10. Never add secrets, API keys, credentials, or environment-specific values.

## Main goal
Create one Core billing abstraction that normalizes the current mixed billing behavior before Core service jobs are exposed to Zalo/Partner clients.

Current billing paths include at least:

- `TodoX.Web/Services/WalletService.cs`
  - direct wallet debit
- `TodoX.Web/Services/AiProviders/AiImageBillingService.cs`
  - reserve / complete / reconciliation semantics
- `billing.token_wallets`
- `billing.token_transactions`
- `billing.token_usage_logs`
- `billing.ai_billing_records`

Do NOT simply call `WalletService.ChargeAsync()` from Core job creation.

## Required deliverables

### 1. Audit existing billing ownership
Trace current code paths and document:

- who estimates customer sell points
- who reserves points
- who charges points
- who refunds/releases points
- how admin/root/system jobs are handled
- how provider retries affect billing
- how image vs video billing differs

Create:

`docs/core-platform/core-billing-ownership.md`

### 2. Introduce Core billing facade
Create transport-neutral abstractions, for example:

- `ICoreBillingService`
- `CoreBillingEstimate`
- `CoreBillingReservation`
- `CoreBillingCompletion`

Exact names may vary, but the semantics must include:

- Estimate
- Reserve
- Complete/Charge
- Release/Refund
- Idempotency
- Insufficient balance
- System/admin no-charge path

The Core layer must not expose provider-specific billing concepts.

### 3. Integrate Core billing with canonical job lifecycle
Update `CoreJobApplicationService` so that:

- service is validated first
- price is estimated before enqueue
- paid customer jobs reserve points before becoming executable
- insufficient balance fails safely
- idempotent duplicate requests return the existing job and do not reserve twice
- job row stores `point_cost_estimate` and appropriate `point_status`

Do not charge final points twice.

### 4. Add lifecycle methods
Extend the Core application layer with transport-neutral methods for:

- list jobs with pagination/filtering
- cancel job
- retry failed job

Rules:

- caller scoping must prevent cross-customer access
- retry must preserve source/service correlation
- retry must not silently double-charge
- cancel must leave billing in a consistent state

### 5. Extend API v1 thin facade
Add API routes only as thin wrappers over Core application services:

- `GET /api/v1/jobs`
- `POST /api/v1/jobs/{jobId}/cancel`
- `POST /api/v1/jobs/{jobId}/retry`

Do not put business logic in endpoint methods.

### 6. Tests
Add focused tests covering at least:

- Zalo/Telegram/Partner/API require idempotency keys
- duplicate create does not reserve twice
- insufficient balance blocks job creation
- system/admin job can be no-charge according to current policy
- caller cannot read/cancel/retry another customer's job
- retry preserves correlation
- cancel billing behavior is deterministic

### 7. Validation
Before declaring completion:

- run `dotnet test`
- run `dotnet build`
- report exact commands and results
- report every changed file
- report any validation that could not be completed

Follow `TodoX.Web/AGENTS.md` strictly.

## Explicit non-goals

Do NOT:

- resume Timelapse development
- modify Timelapse prompts/scenes/workers/finalizer
- migrate `todox` runtime DB into `todo_saas`
- delete legacy billing code
- change provider credentials
- change YEScale/79AI model configuration
- enable `CoreApi:Enabled` by default
- deploy or restart production

## Acceptance criteria

Task is acceptable only when:

1. One Core billing facade exists.
2. `CoreJobApplicationService` no longer creates paid jobs with hard-coded `point_cost_estimate = 0` / `not_required` semantics for customer-facing services.
3. Duplicate external requests are billing-idempotent.
4. Job create/list/get/cancel/retry use caller scoping.
5. No Timelapse execution logic was modified.
6. Build/tests pass, or blockers are explicitly reported.
7. No DB changes were applied automatically.
