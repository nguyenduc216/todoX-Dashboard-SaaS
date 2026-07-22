# Task 4 Legacy Reference Check

Passed:

- Removed tables are absent according to `32_verify_runtime_contract.sql` and `37_verify_task4_runtime.sql`.
- Generic usage service writes `logical_request_id`, `render_job_id`, `provider_task_id`, and `idempotency_key`.
- `IAiProviderService.LogUsageAsync` is now a compatibility facade over `IAiProviderUsageService`.
- Dance Sell usage no longer hashes `danceJob.CustomerId` into bigint for generic usage.

Known limitation:

- `IAiImageBillingService` and image DTO names remain for compatibility. Generic `IAiBillingService` is active in DI, but callers have not all been mechanically renamed in this task.
