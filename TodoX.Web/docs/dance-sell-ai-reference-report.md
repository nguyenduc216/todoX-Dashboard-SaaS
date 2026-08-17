# Dance Sell AI Reference Report

## Scope

- Branch: `integration/rdance-on-construction-video-core`
- Service: customer-facing `Video nhay quang cao thoi trang`
- Internal feature code: `dance_sell`
- Deployment: not deployed; local publish only

## Root Cause

Production `dance_sell.reference_image` was still routed to `local_composite / local_composite`.
That path produced a side-by-side/generated canvas composite from the model image and product image, not a real AI image of the model wearing the product.

The reference generation service also had no completion path for submitted AI reference tasks, so a provider-backed reference version could remain `generating` without being turned into a saved `ready` version.

## Fix

- Added a 79AI reference provider for `dance_sell.reference_image` using the existing 79AI task client.
- Reference submit now uses the production image contract shape:
  - `/generateImage`
  - `base64Image`
  - `image_2`
  - `action_type=create`
  - `editImage=true`
  - `project_id=default`
  - `subjects=[]`
  - `ratio=9:16`
  - `mode=vip`
  - `resolution=2k`
- Added a fashion-reference prompt that explicitly forbids collage, side-by-side layout, inset thumbnails, split canvas, text, watermark, extra people, and duplicated limbs.
- `GenerateAsync` now rejects `local_composite` as the configured product-reference route.
- `AutoPrepareAsync` now polls active reference operations, downloads completed provider output into media storage, completes the reference version, and updates the job to `reference_ready`.
- Failed reference poll results mark both the version and job failed, allowing retry.
- RDance detail page now polls while the reference image is generating and shows animated reference-generation feedback.
- Production route seed now disables the old `local_composite` default and enables `79ai / seedream_5_0`.

## SQL

Manual SQL file:

- `database/manual/rdance-fashion/01_seed_79ai_kling_motion_routes.sql`

Run before live retry/render: YES.

Purpose:

- Disable `dance_sell.reference_image` route `local_composite / local_composite`.
- Set `dance_sell.reference_image` default route to `79ai / seedream_5_0`.
- Preserve `dance_sell.motion_video` route `79ai / kling_video_motion`.

No database schema change is included.

## Timelapse Impact

No Timelapse files were modified.
The fix reuses the existing 79AI task client but keeps all changes isolated to DanceSell/RDance reference routing, UI, repository methods, tests, docs, and manual SQL.

## Validation

- `dotnet test TodoX.Dashboard.sln -c Release --no-restore`: passed, 602 passed
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: passed, 0 warnings, 0 errors
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --include <changed C# files>`: passed
- `git diff --check`: passed, Windows CRLF warnings only
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`: passed

Publish output:

- `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

