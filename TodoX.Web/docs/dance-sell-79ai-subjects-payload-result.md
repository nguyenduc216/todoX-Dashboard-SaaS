# DanceSell 79AI Subjects Payload Result

## Result

DanceSell reference-image submit no longer sends the product image through the
undocumented `image_2` field for `/generateImage`.

IMAGE 1 remains the character/model base edit image in `base64Image`.
IMAGE 2 is serialized into the documented `subjects` form field.

## Outbound Form Fields

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

## Subjects Schema Found

No existing non-empty `subjects` example or object-item schema was found in the
repository or source-controlled docs. The implementation uses the documented
JSON-stringified array mechanism with one product image data URI string:

```json
["data:image/png;base64,<redacted>"]
```

This is recorded in diagnostics as:

`json_stringified_array_of_image_data_uris`

## SQL

Prepared but not executed:

`database/manual/rdance-fashion/02_switch_reference_route_to_gpt_image_2.sql`

The script switches the DanceSell reference route to:

- provider: `79ai`
- model: `imagegen_2_0`
- mode: `medium`
- resolution: `2k`
- ratio: `9:16`

No DB schema change is required.

## Validation

- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore`: passed, 613/613.
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --include <changed C# files>`: passed.
- `git diff --check`: passed, CRLF warnings only.
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`: passed.

Publish output:

`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

## Deploy Readiness

READY TO TEST: YES.

READY TO SWITCH PRODUCTION ROUTE TO `imagegen_2_0`: NO, wait until one live
payload confirms 79AI accepts the `subjects` array item shape.
