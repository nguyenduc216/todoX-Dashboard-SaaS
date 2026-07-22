# Final Provider Render Architecture

Authoritative runtime tables:

- Provider registry/accounts/credentials/leases: `public.todox_ai_provider*`
- Render core: `render.render_jobs`, `render.render_job_steps`, `render.render_job_events`, `render.render_job_inputs`, `render.render_artifacts`
- Usage: `public.todox_ai_provider_usage_log`
- Billing: `billing.ai_billing_records`, `billing.ai_provider_attempts`, wallets and transactions
- Dance Sell business state: `dance_sell.dance_sell_jobs`, `dance_sell.dance_sell_reference_versions`

Runtime services added/activated:

- `IAiRenderCompletionService`
- `IAiOperationLogService`
- `IAiProviderDiagnosticsService`
- `IAiProviderBalanceService`
- `IAiProviderTaskClient`

Event and step taxonomy are centralized in `AiRenderEventTypes` and `AiRenderStepKeys`.
