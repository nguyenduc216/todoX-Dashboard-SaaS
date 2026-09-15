# RVIDEO Service Route Report

## 1. Root Cause

`/create` resolves service cards by the stable `service_type` engine value. The existing `CustomerServiceRouting` branch for `rvideo` intentionally returned no route and the message `Dịch vụ RVideo đang hoàn thiện.`. The catalog item itself was active; the routing helper was the blocker.

## 2. Route Before and After

- Before: RVIDEO -> no route -> coming-soon snackbar.
- After: RVIDEO -> `/jobs/rvideo/new`.
- The route preserves `serviceId` and `serviceCode` query parameters when supplied by the catalog card.

## 3. Job Creation Behavior

Opening `/jobs/rvideo/new` renders the existing native RVIDEO editor on the first `Thông tin` tab. It does not create a database project, enqueue an image/video job, call a provider, charge points, or start AUTO lifecycle processing. The existing `Tạo / phân tách scene` action creates the project after the user supplies valid information.

## 4. Service Context

The selected catalog `serviceId` and `serviceCode` are retained in the route. The native editor receives the service code through the route adapter, so category context is not discarded while the current RVIDEO project schema remains unchanged.

## 5. Files Changed

- `Models/Timelapse/TimelapseModels.cs`
- `Components/Pages/RenderVideoJobs.razor`
- `Components/Pages/RVideoJobCreate.razor`
- `Tests/RVideoFoundationTests.cs`
- `docs/rvideo-service-route-result-report.md`

## 6. Database

- SQL required: NO.
- `/create` reads active cards from `catalog.services`; RVIDEO engine routing is code-controlled by `TodoXServiceEngineTypes.RVideo`.
- No migration or SQL was created or executed.

## 7. Tests

- `dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj --no-restore --logger "console;verbosity=minimal"`
- Passed: 43, Failed: 0.
- Added coverage for the RVIDEO native route, unchanged Timelapse/RDance routes, and unchanged fallback for unrelated unavailable engines.

## 8. Build and Publish

- `dotnet build TodoX.Web.csproj --no-restore`: passed when run sequentially, 0 warnings, 0 errors.
- `git diff --check`: passed.
- `dotnet publish TodoX.Web.csproj --no-restore -c Release`: succeeded.
- Publish output: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\TodoX.Web\bin\Release\net10.0\publish`.

## 9. Compatibility

- Timelapse routing unchanged.
- RDance routing unchanged.
- Unrelated coming-soon behavior unchanged.
- Opening RVIDEO does not start rendering or provider work.
- No changes to RDance, Construction Timelapse, pricing, provider configuration, billing, n8n, or Telegram flows.
