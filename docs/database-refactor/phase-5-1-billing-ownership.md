# Phase 5.1 Billing Ownership

Generic billing now owns the production write path:

- `IAiBillingService` -> `AiBillingService`
- `IAiBillingRepository` -> `AiBillingRepository`
- Generic tables: `billing.ai_billing_records`, `billing.ai_provider_attempts`, `billing.token_wallets`, `billing.token_transactions`

`AiImageBillingService` is now an obsolete compatibility adapter. Source tests verify it has no Dapper import, no `TodoXConnectionFactory`, no direct `OpenAsync`, and no direct billing table references.

Remaining blocker: `AiImageBillingDashboardService` remains a legacy-named read adapter over generic billing tables. It is read-only, but the naming should be cleaned in a later non-risky commit before declaring full production readiness.
