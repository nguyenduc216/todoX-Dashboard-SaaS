# todoX Core Platform / API Architecture

Status: Phase 1 foundation

## Goal

Build one TodoX application core that can be called by Dashboard, Zalo Mini App, Telegram and future partner/external clients without duplicating business logic.

## Non-negotiable compatibility rules

1. Dashboard must not depend on a public API version. Dashboard backend and public API call the same application/core services.
2. Public API is versioned (`/api/v1/...`). Breaking transport changes create a new API version; they do not change core service contracts unnecessarily.
3. `todo_saas` is the business/control-plane source of truth for identity, catalog, canonical jobs, pricing and wallet.
4. `todox` remains the legacy execution/workflow database during migration. Existing Timelapse/RVideo/RDance workflow internals are not rewritten in Phase 1.
5. Every legacy execution is reached through an `ICoreJobExecutionAdapter`.
6. Client channel (`dashboard`, `zalo`, `telegram`, `partner`, `api`, `system`) is audit/routing metadata only. Business rules must not be duplicated per channel.
7. `render.render_jobs` is the canonical platform job candidate. Do not create another generic jobs table.
8. New service-specific forms are catalog-driven. Clients must not hard-code a separate form definition when a shared catalog definition is available.
9. Secrets/credentials are resolved by transport/provider infrastructure and are never included in core job payloads.
10. No existing Timelapse workflow behavior is changed until its adapter migration is explicitly started.

## Runtime target

```text
Dashboard backend ----\
Zalo Mini App ---------\
Telegram --------------- > TodoX Core/Application -> render.render_jobs -> RenderJobWorker
Partner/API ------------/                                      |
                                                               v
                                                     CoreServiceJobHandler
                                                               |
                                                               v
                                                     CoreExecutionRouter
                                                   /        |         \
                                          TimelapseAdapter RVideoAdapter RDanceAdapter
                                                   \        |         /
                                                               v
                                                     legacy todox / n8n
```

## Canonical channel contract

Allowed values:

- `dashboard`
- `zalo`
- `telegram`
- `partner`
- `api`
- `system`

Transport layers resolve identity and provide `CoreRequestContext`. Core services never parse Zalo tokens, Telegram chat IDs, dashboard cookies or partner API keys.

## Canonical service contract

`catalog.services` remains the canonical catalog. Phase 1 uses:

- `service_code`: stable machine identifier
- `service_name`: display name
- `service_type`: business/service family
- `workflow_code`: legacy/execution hint only
- `status`: availability
- `default_options.form_schema`: shared dynamic form schema

A future normalized `service_fields` table may be introduced only if the JSON form schema becomes insufficient. Clients must consume the application projection rather than query database tables directly.

## Canonical job contract

The existing `render.render_jobs` table already contains the fields required for the first platform migration, including:

- `service_id`
- `logical_request_id`
- `job_type`
- `operation_type`
- `source_type`
- `input_json`
- `prompt_json`
- `reference_json`
- `output_json`
- provider/model fields
- point/billing fields
- retry/error/timestamps

Phase 1 introduces `job_type = core_service` as the single worker entry point for catalog-driven services. Service-specific dispatch occurs through the adapter router.

## Idempotency

External callers must send an idempotency key for create/retry-sensitive operations. The target persistence key is `render.render_jobs.logical_request_id`, scoped by caller/customer as appropriate. A repeated request must return the existing canonical job rather than create duplicate paid AI work.

The create endpoint must not be enabled for external clients until this lookup/write behavior and client authentication are implemented together.

## Dynamic form example

Stored under `catalog.services.default_options.form_schema`:

```json
{
  "version": 1,
  "fields": [
    {
      "key": "source_image",
      "type": "image",
      "label": "Ảnh đầu vào",
      "required": true
    },
    {
      "key": "scene_count",
      "type": "select",
      "label": "Số scene",
      "options": [3, 4, 5, 6],
      "default": 4
    }
  ]
}
```

Dashboard, Zalo and partner clients render the same schema.

## Migration order

1. Core contracts, channel context, service catalog projection, adapter router.
2. Canonical Core Job application service with idempotency and source/service metadata.
3. Authentication/client resolver for Dashboard session, Zalo, Telegram binding and partner API credentials.
4. `/api/v1/services` and `/api/v1/jobs` transport endpoints.
5. Timelapse adapter (reuse current engine/workflows).
6. End-to-end dual-run verification.
7. RVideo adapter.
8. RDance adapter.
9. Only after legacy adapters are stable, consider consolidating execution data from `todox` into `todo_saas`.

## API compatibility policy

- Additive fields may be added to v1 responses.
- Existing v1 field meaning must not change.
- Breaking field/behavior changes require `/api/v2`.
- Dashboard does not call `/api/v1` internally; it calls the same application services used by v1.
- Execution adapter contracts are internal and may evolve independently of public API versions.
