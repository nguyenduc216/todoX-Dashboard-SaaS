# DanceSell 79AI Reference Payload Audit

## Scope

- Branch: `integration/rdance-on-construction-video-core`
- Endpoint: `POST /generateImage`
- Operation: DanceSell fashion try-on reference image

## Corrected Outbound Form Fields

The DanceSell reference request now sends these fields:

- `access_token`
- `domain`
- `model`
- `prompt`
- `base64Image`
- `action_type`
- `editImage`
- `project_id`
- `subjects`
- `ratio`
- `resolution`
- `mode`

`base64Image` carries IMAGE 1, the character/model edit base. `subjects` carries
IMAGE 2, the product/clothing reference. The request never sends `image_2`.

## Subjects Transport

Repository audit found no existing non-empty `subjects` payload and no provider
documentation in source control that specifies an object-shaped subject item. The
adapter therefore uses the documented JSON-stringified array transport with one
image data URI string per subject:

```json
["data:image/png;base64,<product-bytes>"]
```

This transport is isolated to the DanceSell `/generateImage` adapter and is recorded
as `json_stringified_array_of_image_data_uris` in sanitized diagnostics. It must be
verified with a live 79AI request before the separate route-switch SQL is executed.

## Safe Diagnostics

The stored request metadata and application log include:

- model
- SHA-256 prompt hash and prompt length
- `editImage`
- base image presence, MIME type, and byte count
- subject count, MIME types, and byte counts
- ratio, resolution, and mode
- final form-urlencoded field names

They exclude access-token values and all base64 image values.

## Route Change

`database/manual/rdance-fashion/02_switch_reference_route_to_gpt_image_2.sql`
is an unexecuted manual script that changes only the DanceSell reference-image route
from `79ai / seedream_5_0` to `79ai / imagegen_2_0` with:

- `mode=medium`
- `resolution=2k`
- `ratio=9:16`

No schema change is required.
