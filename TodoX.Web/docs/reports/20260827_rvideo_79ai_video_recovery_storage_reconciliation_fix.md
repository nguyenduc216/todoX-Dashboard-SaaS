# RVideo 79AI video recovery, storage key, and reconciliation fix

Date: 2026-08-27

## Summary

Updated the RVideo scene-video flow so completed videos can be recovered idempotently, storage keys stay immutable per video version, and 79AI reconciliation follows the video-specific recovery path without resubmitting provider jobs.

## Changed behavior

- Scene video storage now uses a version-scoped immutable object key.
- Scene video recovery can locate a version by logical request id.
- Media saving tolerates idempotent retries at the same object key.
- Scene video worker reuses an existing version when the logical request id already exists.
- 79AI video reconciliation now polls the 79AI video service and completes or fails the version from the recovered record.
- Video billing uses the video-specific insufficient-points message.

## Validation

- `dotnet build .\\TodoX.Web.csproj -c Release --no-restore` succeeded.
- `dotnet test .\\Tests\\TodoX.Web.Phase1B.Tests.csproj --filter "FullyQualifiedName~RVideoProviderPollingRegressionTests|FullyQualifiedName~RVideoVideoHotfixTests" -v minimal` succeeded.
- `dotnet test .\\Tests\\TodoX.Web.Phase1B.Tests.csproj -v minimal` reported unrelated existing failures outside this change set.
- `dotnet format .\\TodoX.Web.csproj --verify-no-changes` reported pre-existing whitespace issues in unrelated files.

## Notes

- No database migration was added or executed.
- Publish output path used: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`
