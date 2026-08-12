# 79AI Video Catalog Audit

## Audit Method

- Used the existing TodoX secure credential resolver and `Ai79CatalogClient`.
- Called the implemented 79AI catalog contract: `POST /models` with `domain=79ai.net` and `type=video`.
- No access token, credential payload, or raw provider response is stored in this report.

## Live Result

- Catalog configured: yes.
- Catalog path: `/models`.
- Total models returned by image + video fetch: 48.
- Video models returned: 25.
- Grok returned: yes.
- Kling returned: yes.
- Seedance returned: yes.
- VEO variants returned: yes.

## Video Models

| Code | Display | id_base | Server | Status | Modes | Durations | Resolutions | Price rows |
|---|---|---|---|---|---|---|---|---:|
| `flux_3` | FLUX 3 | `flux_3` | fluxai | ON | vip | 5-20 | 720p, 1080p | 32 |
| `grok_video_heavy` | Grok Video - Heavy | `v45gg33` | grokai | ON | - | 6, 10, 12, 15 | 720p, 1080p | 6 |
| `hailuo_2_3` | Hailuo 2.3 | `a79dae13b3b63262` | hailuoai | ON | fast, quality, relax | 6, 10 | 720p, 1080p | 7 |
| `happy_horse_1` | Happy Horse - 1 | `happy_horse_1` | wanai | ON | - | 3-15 | 720p, 1080p | 26 |
| `kling_video_2_1_10s` | Kling - 2.1 - 10s - FULL HD | `7a01867891eea169` | klingai | ON | professional, standard | - | - | 2 |
| `kling_video_2_1_5s` | Kling - 2.1 - 5s - FULL HD | `cd8aee41374b2bd1` | klingai | ON | professional, standard | - | - | 2 |
| `kling_video_lipsync` | Kling - LipSync | `a416610b612ceb99` | klingai | ON | - | - | - | 0 |
| `kling_video_2_5` | Kling 2.5 | `3da49dd3217cfeeb` | klingai | ON | professional, relax, standard | 5, 10 | - | 6 |
| `kling_video_2_6` | Kling 2.6 | `23e92524f2e554a1` | klingai | ON | professional, professional_audio, standard | 5, 10 | - | 6 |
| `kling_video_motion` | Kling 2.6 - Motion Control | `bert43634` | klingai | ON | professional, standard | - | - | 2 |
| `kling_video_3_0_edit` | Kling 3.0 - Edit | `kling-o3-edit` | klingai | ON | professional, standard | 3-10 | - | 16 |
| `kling_video_motion_3` | Kling 3.0 - Motion Control | `kling_video_motion_3` | klingai | ON | professional, standard | - | - | 2 |
| `kling_video_3_0` | Kling 3.0 - Omni | `cdsgf354354` | klingai | ON | professional, professional_4k, professional_vip, professional_vip_4k, standard, standard_vip | 3-15 | - | 78 |
| `kling_video_o1` | Kling O1 | `0654474ff8df9b30` | klingai | ON | professional, standard | 3-10 | - | 16 |
| `kling_video_o1_edit` | Kling O1 - Edit | `o1g4555` | klingai | ON | professional | 3-10 | - | 8 |
| `minimax_h3` | Minimax H3 | `minimax_h3` | hailuoai | ON | vip | 5-15 | 2k | 11 |
| `omnihuman_1_5` | OmniHuman 1.5 | `7108f417707d1d5c` | bytedanceai | ON | turbo | - | - | 1 |
| `seedance_20_pro` | Seedance 2.0 | `seedance_40_pro` | bytedanceai | ON | fast, fast_2, professional, professional_2 | 4-15 | 480p, 720p, 1080p | 96 |
| `seedance_20_mini` | Seedance 2.0 - Mini | `seedance_20_mini` | bytedanceai | ON | business_mini | 4-15 | 480p, 720p | 24 |
| `seedance_20_pro_edit` | Seedance 2.0 - Omni | `seedance_20_pro_edit` | bytedanceai | ON | business_fast, business_fast_vip, business_professional, business_professional_vip, fast_cheap | 4-15 | 720p, 1080p, 4k | 155 |
| `seedance_25_omni` | Seedance 2.5 - Omni | `seedance_25_omni` | bytedanceai | ON | business_professional, business_professional_vip | 4-30 | 480p, 720p | 108 |
| `veo_omni` | VEO - Omni | `veo_omni` | google_veo | ON | flash | 4, 6, 8, 10 | 720p, 1080p, 4k | 12 |
| `veo_3_1` | VEO 3.1 | `38b4b30c4fe494de` | google_veo | ON | fast, lite, quality | - | 720p, 1080p, 4k | 9 |
| `video_upscale_1_0` | Video Upscale 1.0 | `video_upscale_1_0` | autoai | ON | professional | - | 1080p, 2k, 4k | 3 |
| `wan_2_2` | Wan 2.2 Animate | `9822798ed602d820` | wanai | ON | fast, relax | - | 480p, 720p | 4 |

## Findings

- Grok exists in the live catalog as `grok_video_heavy`.
- VEO is returned as separate provider model codes: `veo_omni` and `veo_3_1`.
- VEO Fast/Lite are not separate provider model codes in the live response; they are provider modes on `veo_3_1`.
- Seedance modes, durations, resolutions, and prices are returned by the live provider. The parser must preserve these values instead of relying on pre-seeded rows.
- Production likely showed only a small subset because the existing UI defaulted to enabled-only model filtering and previous parsing was less tolerant of provider aliases/nested option shapes.

## Source Fixes

- Parser now reads additional safe aliases such as `model_name`, `model_id`, `model_key`, `label`, `modality`, `provider_server`, and nested `variants`/`options`/`variant_options`/`price_options`.
- Sync still uses `provider_id + provider_model_code` as canonical identity.
- Sync records sanitized ignored diagnostics for invalid/no model code and duplicate provider model code.
- No fake provider pricing was added. Prices come from live catalog or the existing verified VEO Omni fallback only.
