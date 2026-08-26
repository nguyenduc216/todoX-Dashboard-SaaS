# RDance Banana 2K Reference Route Report

## Scope

Updated only the RDance / RDance Fashion reference image generation step:

`Character Image + Product Image -> Generate / Compose Reference Image`

Motion video generation, Kling Motion routing, billing, Telegram, Timelapse, RVideo, and unrelated workflows were not changed.

## Model Routing

- Old default reference model: `79ai / imagegen_2_0`
- New default reference model: `79ai / google_image_gen_banana_2`
- Display name: `Banana 2K Fashion Reference`
- Capability: `reference_image_generation`
- Route config: `/generateImage`, poll `/image`, `subject_schema=form_subject_url_fields`, `domain=79ai.net`, `project_id=default`, `action_type=create`, `sync=false`, `ratio=16:9`, `category=FASHION`, `mode=vip`, `resolution=2k`, `num_outputs=1`, `language=VI`
- Fallback retained: `79ai / imagegen_2_0` as non-default route with `provider_error` and `timeout` fallback metadata.

## Behavior

- Person + Product uses Banana 2K and sends `subjects[0][url]` for the character image plus `subjects[1][url]` for the product image.
- Person Only keeps the current behavior: it uses the character image directly as the approved reference and does not submit a Banana 2K image-generation task.
- No null or empty product subject is sent.
- Prompt semantics were preserved; only model/default route and Banana 2K mode/resolution were changed.

## Database

- Migration required: **NO**
- Schema changes: none
- Updated idempotent manual route SQL only; no SQL was executed.

## Validation

- `dotnet format ..\TodoX.Dashboard.sln --verify-no-changes --no-restore --include ...`: PASS
- `git diff --check`: PASS
- Replacement-character scan on changed files: PASS
- `dotnet build ..\TodoX.Dashboard.sln -c Release --no-restore`: PASS, 0 errors; existing warnings remain.
- `dotnet test ..\TodoX.Dashboard.sln -c Release --no-restore`: PASS, 771 passed, 0 failed, 0 skipped.
- `dotnet publish TodoX.Web.csproj -c Release --no-restore -o ..\artifacts\publish\todox-dashboard`: PASS

Publish output:

`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

## Acceptance Criteria

- RDance Person + Product defaults to Banana 2K for reference composition: PASS
- Person Only does not call Banana 2K: PASS
- Kling Motion unchanged: PASS
- No unrelated module changes: PASS
- Build/test pass: PASS

