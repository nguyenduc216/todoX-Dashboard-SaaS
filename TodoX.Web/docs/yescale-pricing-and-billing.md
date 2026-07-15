# YEScale Pricing And Billing

MCP queried at UTC: `2026-07-15T18:53:03.6516197Z`.

Tools used:

- `yescale_list_models` with `category=Image`
- `yescale_list_models` with `query=nano-banana-2`
- `yescale_list_models` with `query=gpt-image`
- `yescale_list_models` with `query=seedream-5`
- `yescale_get_model_doc`
- `yescale_get_task_model_config`
- `yescale_get_current_user`
- `yescale_get_workspace_overview`

## Verified Production Tariffs

| Role | Model | Production payload | YEScale price | TodoX cost points |
| --- | --- | --- | --- | --- |
| Default | `nano-banana-2` | `size=1K` | `$0.08/request` | `0.064` |
| Cheap selectable | `gpt-image` | `quality=low`, `size=1024x1024` | `$0.024/request` | `0.0192` |
| Fallback | `seedream-5` | `size=2K` | `$0.065/request` | `0.052` |

Formula:

```text
provider_cost_points = provider_cost_usd * 8000 / 10000
provider_cost_points = provider_cost_usd * 0.8
```

Keep these as database/config values. Do not hard-code customer prices, exchange rate, or point value in UI or provider adapters.

## Cost Fields

- `provider_estimated_cost_usd`: tariff-derived value for the planned payload.
- `provider_actual_cost_usd`: provider-returned actual cost, only when YEScale returns one in task/ledger data.
- `customer_charged_points`: points charged to the customer by TodoX policy.
- `cost_source`: `configured_tariff` until YEScale task/ledger actual-cost reconciliation is implemented.

Current `AiProviderUsageLog` supports decimal point fields (`decimal` for quantity, unit cost, total points, provider raw cost). Existing DB column precision must be verified by manual SQL before production enablement.

## Balance Endpoint

MCP exposed account/usage tools, not a documented task API endpoint named `balance`.

Observed tools:

- `yescale_get_current_user`: returns quota/remain quota fields for the configured MCP access context.
- `yescale_get_workspace_overview`: returned unauthorized for the current token during verification.
- `yescale_get_usage`, `yescale_search_billing_ledger`, and control-plane usage tools exist for reconciliation.

Do not display a fabricated YEScale balance in TodoX. A production balance dashboard needs a server-side service backed by an MCP-verified or YEScale-documented API available to the app access key.

## Charging Policy

- Customer requests should charge exactly once per TodoX request.
- Fallback attempts must not create multiple independent wallet debits for the same logical render.
- Admin/system renders are billing-exempt for customer points but must still log provider cost with metadata.
- If YEScale does not return actual task cost, use the configured tariff and set `cost_source=configured_tariff`.
- If YEScale later exposes actual charged cost through ledger/task detail, reconcile provider actual cost separately from customer charge.

## Production Blockers

Before declaring full production-ready billing:

- Confirm database numeric precision for wallet balance, ledger points, `unit_cost_points`, and `total_points`.
- Add/verify idempotent wallet reservation/debit linkage for all image router paths.
- Implement actual-cost reconciliation if YEScale task detail or billing ledger returns charged amount by `request_id` or `task_id`.
- Implement admin exemption metadata fields or store equivalent data in `metadata_json`.
