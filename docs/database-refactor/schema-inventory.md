# Schema inventory

Generated: 2026-07-22T14:13+07:00

## Environment

- Database: `todo_saas`
- PostgreSQL: `17.10`
- Connected user: `postgres`
- Search path: `"$user", public`
- Application environment source checked: `ASPNETCORE_ENVIRONMENT` was not set in process environment; launch profile uses Development.
- Background workers registered in code: `AiImageBillingReconciliationWorker`, `RenderJobWorker`, `SceneVideoJobWorker`, `ReupCampaignWorker`.

## Object summary

| Schema | Objects | Tables | Rows (estimated) |
| --- | ---: | ---: | ---: |
| $(Microsoft.PowerShell.Commands.GroupInfo.Name) | 5 | 5 | 343 |
| $(Microsoft.PowerShell.Commands.GroupInfo.Name) | 1 |  | 0 |
| $(Microsoft.PowerShell.Commands.GroupInfo.Name) | 22 | 15 | 98 |
| $(Microsoft.PowerShell.Commands.GroupInfo.Name) | 10 | 10 | 411 |
| $(Microsoft.PowerShell.Commands.GroupInfo.Name) | 8 | 8 | 45 |

## Key tables

| Table | Rows |
| --- | ---: |
| $(@{table_name=billing.ai_image_billing_records; row_count=15}.table_name) | 15 |
| $(@{table_name=billing.ai_image_provider_attempts; row_count=15}.table_name) | 15 |
| $(@{table_name=billing.token_transactions; row_count=10}.table_name) | 10 |
| $(@{table_name=billing.token_usage_logs; row_count=301}.table_name) | 301 |
| $(@{table_name=billing.token_wallets; row_count=2}.table_name) | 2 |
| $(@{table_name=dance_sell.dance_sell_jobs; row_count=0}.table_name) | 0 |
| $(@{table_name=render.render_artifacts; row_count=0}.table_name) | 0 |
| $(@{table_name=render.render_events; row_count=0}.table_name) | 0 |
| $(@{table_name=render.render_job_events; row_count=58}.table_name) | 58 |
| $(@{table_name=render.render_job_inputs; row_count=0}.table_name) | 0 |
| $(@{table_name=render.render_job_snapshots; row_count=0}.table_name) | 0 |
| $(@{table_name=render.render_job_steps; row_count=0}.table_name) | 0 |
| $(@{table_name=render.render_jobs; row_count=12}.table_name) | 12 |
| $(@{table_name=render.render_scenes; row_count=0}.table_name) | 0 |
| $(@{table_name=todox_ai_character; row_count=7}.table_name) | 7 |
| $(@{table_name=todox_ai_character_reference; row_count=0}.table_name) | 0 |
| $(@{table_name=todox_ai_character_render; row_count=47}.table_name) | 47 |
| $(@{table_name=todox_ai_provider; row_count=5}.table_name) | 5 |
| $(@{table_name=todox_ai_provider_capability; row_count=35}.table_name) | 35 |
| $(@{table_name=todox_ai_provider_usage_log; row_count=0}.table_name) | 0 |
| $(@{table_name=token_transactions; row_count=0}.table_name) | 0 |
| $(@{table_name=token_wallets; row_count=0}.table_name) | 0 |

## Supporting CSV files

- `database-object-inventory.csv`
- `columns-inventory.csv`
- `indexes-before.csv`
- `constraints-before.csv`
- `row-counts-before.csv`
