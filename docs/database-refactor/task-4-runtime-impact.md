# Task 4 Runtime Impact

Main impact areas:

- Billing: generic contracts were added in `AiBillingService.cs`; image-oriented interfaces remain as compatibility adapters for existing workflows.
- Usage: `IAiProviderUsageService` and `IAiProviderUsageRepository` now write to `public.todox_ai_provider_usage_log` with idempotency.
- Dance Sell: usage logs now use `danceJob.CustomerId` as UUID, plus render job and provider task identifiers.
- Image router: usage logs include provider task, render job, raw request/response/usage JSON, and provider cost currency when known.
- Database: Task 4 SQL adds missing billing/usage contract columns, state checks, and indexes without data deletion.

Active runtime removed-table references remain blocked by Task 3/4 tests and `37_verify_task4_runtime.sql`.
