# Core Billing Ownership

Status: Phase 1 Core Platform contract.

## Existing Owners

`ServiceSellPriceResolver` owns server-side customer sell-point estimation for catalog services. It reads active rows from `catalog.service_sell_prices` through the catalog repository and resolves image/video-scene price rows by service, quality tier and duration.

`WalletService` owns the legacy direct-debit path. It deducts points immediately from `billing.token_wallets`, writes `billing.token_transactions`, and logs provider usage in `billing.token_usage_logs`. Legacy admin/root calls can be logged without charging a customer wallet.

`AiImageBillingService` owns the provider-image reserve/complete/reconciliation flow. It creates idempotent billing records keyed by `logical_request_id`, reserves points by moving wallet balance into `locked_balance`, completes successful renders with a final debit transaction, and releases reservations when provider work fails or requires reconciliation.

`AiBillingPayerResolver` owns image-billing payer resolution. Authenticated customers pay through their own customer wallet. Root/system-wallet-authorized operators and trusted background contexts may use the configured system image wallet for provider image flows.

## Core Facade Ownership

`ICoreBillingService` is the transport-neutral facade used by `CoreJobApplicationService`. It does not expose provider-specific records, credentials, model contracts or provider cost details.

The canonical Core billing state is stored on `render.render_jobs`:

- `point_status='pending'`: customer points are reserved.
- `point_status='charged'`: reserved points have been finalized as charged.
- `point_status='insufficient'`: reservation failed safely before execution.
- `point_status='cancelled'`: pending reservation was released.
- `point_status='refunded'`: charged points were refunded.
- `point_status='not_required'`: free or trusted internal work did not require a customer charge.

The facade estimates, reserves, completes and releases/refunds against the canonical job row. Wallet and job updates happen inside tenant-scoped transactions with row/advisory locks so repeated calls do not double charge.

## Admin/System Behavior

Trusted internal no-charge behavior requires both:

- `CoreRequestContext.Channel == system`
- `CoreRequestContext.IsTrustedInternal == true`

User-supplied `system` channel alone does not broaden access or skip billing. Public API transport authenticators must resolve and mark trusted internal contexts explicitly.

## Retry Behavior

Retry starts from a failed canonical job. The source job's pending reservation is released or charged amount refunded through the Core facade before creating the retry. The retry stores `retry_of_job_id` and receives a new scoped logical request identity, so repeated retry requests can be idempotent without reusing the source job identity.

## Image vs Video Billing

Image and video-scene sell prices remain catalog-driven through `catalog.service_sell_prices`. Generic Core inputs expose counts and duration (`imageCount`, `sceneCount`, `durationSeconds`) while service-specific execution adapters remain responsible for provider payloads in later phases.

Provider image billing still remains in `AiImageBillingService` for existing provider workflows. Core does not replace provider reconciliation in this phase; it only normalizes the platform job billing lifecycle before execution adapters are connected.

