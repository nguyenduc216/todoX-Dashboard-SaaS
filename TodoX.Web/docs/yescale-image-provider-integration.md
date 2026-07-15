# YEScale Image Provider Integration

Queried at UTC: `2026-07-15T17:58:32.0838982Z`

Authoritative source: YEScale MCP via `https://yescale.io/mcp`.

MCP tools called:

- `yescale_list_models`
- `yescale_get_model_doc`
- `yescale_get_task_model_config`

Verified image models:

| Routing role | Model ID | Protocol | Endpoint | Async | Input | Output | Required params |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Default | `nano-banana-2` | YEScale task | `/task/submit`, poll `/task/{task_id}` | Yes | text, image | image | `prompt`, `config.size`; optional `config.images`, `config.aspect_ratio`, `config.google_search`, `config.thinking` |
| Cheap | `gpt-image` | YEScale task | `/task/submit`, poll `/task/{task_id}` | Yes | text, image | image | `prompt`, `config.size`; optional `config.images`, `config.quality`, `config.background`, `config.mask` |
| Backup | `seedream-5` | YEScale task | `/task/submit`, poll `/task/{task_id}` | Yes | text, image | image | `prompt`, `config.size`; optional `config.images`, sequential image generation config |

Reference image support is verified for all three models through `config.images`. URLs must be publicly renderable; `seedream-5` also accepts base64 data URLs per MCP config.

Pricing and limits must remain DB/config data. MCP returned request pricing/throughput in the saved verification artifact; do not hard-code sale points in UI or code. The current manual seed keeps `unit_cost_points = 0`, disabled, and not default until the owner approves customer-facing point charges.

## TodoX Routing Trace

| TodoX capability/path | Current provider route | YEScale integration status | Database update |
| --- | --- | --- | --- |
| `avatar_generation` admin preview | `AvatarTemplateService` now routes `openrouter_image` and `yescale_task_image` through `AiImageRenderRouter` | Ready when provider capability is enabled/configured | Yes, manual seed row required |
| `avatar_generation` public builder | Router only when `Features:AvatarBuilderUseImageRouter=true`; now supports `yescale_task_image` | Staging-ready behind existing feature flag | Yes, manual seed row required |
| `scene_image_generation` scene manual rerender | `SceneImageRenderService` routes through `AiImageRenderRouter` and accepts YEScale | Ready when provider capability is enabled/configured | Yes, manual seed row required |
| `scene_image_generation` scene batch auto render | `SceneImageBatchRenderHandler` calls `RenderSceneImageAsync` through the shared router | Ready when provider capability is enabled/configured | Yes, manual seed row required |
| `character_generation` | `AiCharacterService` direct provider call now passes provider/capability JSON to YEScale | Ready when provider capability is enabled/configured | Yes, manual seed row required |
| `image_generation` generic router | `AiImageRenderRouter` | Already supported | Yes, manual seed row exists |
| `thumbnail_generation` marketing thumbnail without fixed-asset composite | `ServiceThumbnailRenderService` routes through `AiImageRenderRouter` | Ready when provider capability is enabled/configured | Yes, manual seed row required |
| `poster_generation` / fixed-asset composite thumbnail | `ServiceThumbnailRenderService` -> `IImageRenderService` for `background_then_composite` | Intentional legacy path: model background plus code composite/text overlay requires a separate router-compatible composite design | Not enabled as full YEScale default yet |

Capability duplication to consolidate:

- Scene image rendering now uses `scene_image_generation`; keep old `avatar_generation` rows only for avatar/avatar-builder use.
- `avatar_generation` and `chibi_avatar_generation` overlap; keep `avatar_generation` as the routed canonical capability unless product needs separate pricing.
- `poster_generation`, `thumbnail_generation`, and generic `image_generation` overlap in model needs but differ in business metering. Keep separate capability rows for pricing, but use the same YEScale provider adapter.

## Rollout Order

1. Deploy code with YEScale support disabled.
2. Configure `AiProviders__YEScale__AccessKey` in the secret store/environment only.
3. Run the disabled manual SQL seed in staging.
4. Enable one non-production capability row at a time, starting with `image_generation` or admin `avatar_generation`.
5. Validate render success, usage log, point charge, and fallback metadata.
6. Promote `avatar_generation` public builder only with `Features:AvatarBuilderUseImageRouter=true`.
7. Defer fixed-asset composite poster default until the composite pipeline is migrated.

Rollback is data-only while code remains compatible: disable `yescale_task_image` capabilities and unset `is_default`.
