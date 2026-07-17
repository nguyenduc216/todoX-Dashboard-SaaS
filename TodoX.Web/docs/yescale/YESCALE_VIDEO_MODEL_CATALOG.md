# YEScale Video Model Catalog

Queried at UTC: `2026-07-17T18:12:48.769Z` to `2026-07-17T18:12:49.501Z`

Authoritative source: YEScale MCP

MCP tools called:

- `yescale_list_models(query="grok-video", includeDeprecated=true)`
- `yescale_list_models(query="omni-flash", includeDeprecated=true)`

Notes:

- This document only records metadata verified from MCP on July 17, 2026.
- Where MCP did not return a field, the value is marked `MCP không cung cấp`.
- Runtime app access keys are separate from the MCP control-plane access token. Runtime must still read `AiProviders__YEScale__AccessKey`.

## Common task transport

| Field | Verified value |
| --- | --- |
| Base URL | `https://api.yescale.io` |
| Alternate base URL seen in prior verified MCP docs | `https://api.yescale.vip` |
| Submit endpoint | `/task/submit` |
| Poll endpoint template | `/task/{task_id}` |
| Auth header | `Authorization: Bearer {api_key}` |
| Transport style | async task submit + poll |
| Runtime secret source | `AiProviders__YEScale__AccessKey` |
| MCP fetched cached? | `false` |

## Model: `grok-video`

| Field | Verified value |
| --- | --- |
| Provider | `xAi` |
| Category | `Video` |
| Billing type | `pay-per-request` |
| Input | `text`, `image` |
| Output | `video` |
| Required payload | `model`, `prompt`, `config.aspect_ratio`, `config.duration` |
| Optional payload | `config.images`, `config.size` |
| Supported aspect ratios | `2:3`, `3:2`, `16:9`, `9:16`, `1:1` |
| Supported durations | `4`, `6`, `8`, `10`, `12`, `15` seconds |
| Supported size | `720P`, `1080P` |
| Terminal statuses verified from MCP/docs used in runtime | `SUCCESS`, `FAILURE` |
| Throughput | `60` |
| Notes | MCP list response notes: `Supports 6s, 10s, 15s duration` |

Pricing verified from MCP:

| Match | YEScale USD / request |
| --- | --- |
| `duration=4` | `0.14` |
| `duration=6` | `0.14` |
| `duration=8` | `0.17` |
| `duration=10` | `0.20` |
| `duration=12` | `0.25` |
| `duration=15` | `0.30` |

Not provided by MCP:

- exact output JSON field schema for success payload
- output URL expiry policy
- explicit rate-limit headers/policy
- exact retry contract for transient poll errors

## Model: `grok-video-1.5`

| Field | Verified value |
| --- | --- |
| Provider | `xAi` |
| Category | `Video` |
| Billing type | `pay-per-request` |
| Input | `text`, `image` |
| Output | `video` |
| Required payload | `model`, `prompt`, `config.images`, `config.aspect_ratio`, `config.duration`, `config.size` |
| Image constraint | exactly one source image |
| Supported aspect ratios | `16:9`, `9:16` |
| Supported durations | `4`, `6`, `8`, `10`, `12`, `15` seconds |
| Supported size | `720P` |
| Terminal statuses verified from MCP/docs used in runtime | `SUCCESS`, `FAILURE` |
| Throughput | `60` |
| Notes | `single-image-to-video`; source image becomes first frame |

Pricing verified from MCP:

| Match | YEScale USD / request |
| --- | --- |
| `duration=4` | `0.20` |
| `duration=6` | `0.20` |
| `duration=8` | `0.26` |
| `duration=10` | `0.30` |
| `duration=12` | `0.35` |
| `duration=15` | `0.40` |

Not provided by MCP:

- exact submit response schema beyond `task_id`
- output URL expiry policy
- actual billing usage JSON field names
- explicit poll timeout recommendation

## Model: `omni-flash`

| Field | Verified value |
| --- | --- |
| Provider | `Google` |
| Category | `Video` |
| Billing type | `pay-per-request` |
| Input | `text`, `image`, `video` |
| Output | `video` |
| Required payload | `model`, `prompt`, `config.aspect_ratio`, `config.mode` |
| Optional payload | `config.images`, `config.videos` |
| Supported aspect ratios | `16:9`, `9:16` |
| Supported modes | `t2v`, `i2v(img_ref)`, `i2v(first_last_frame)`, `v2v` |
| Image limit | up to `5` images |
| Video limit | up to `1` video |
| Throughput | `0` returned by MCP list call |
| Notes | `CDN media-only Omni Flash no-watermark video model.` |

Pricing verified from MCP:

| Match | YEScale USD / request |
| --- | --- |
| `mode=t2v` | `0.37` |
| `mode=i2v(img_ref)` | `0.37` |
| `mode=i2v(first_last_frame)` | `0.37` |
| `mode=v2v` | `0.47` |

TodoX runtime note:

- Current TodoX scene-video runtime only maps `image_to_video`, so the supported `omni-flash` mode for this path is `i2v(img_ref)`.

Not provided by MCP:

- exact response body field used for output video URL
- output duration/resolution defaults
- URL expiry policy
- explicit poll-status enumeration beyond what prior docs/runtime already verified

## Runtime mapping for TodoX

| TodoX capability | YEScale provider code | Verified models |
| --- | --- | --- |
| `image_to_video` | `yescale_task_video` | `grok-video`, `grok-video-1.5`, `omni-flash` |

Current runtime assumptions implemented on July 17, 2026:

- `grok-video` uses source image + prompt + aspect ratio + duration + optional size.
- `grok-video-1.5` uses exactly one source image and `720P`.
- `omni-flash` is wired only for `i2v(img_ref)` in the TodoX scene-video flow.
