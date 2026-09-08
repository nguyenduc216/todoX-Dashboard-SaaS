# Admin Job Monitoring Dashboard

## Scope

Implemented the read-only administrator dashboard at `/admin/job-monitor`, matching
the supplied `mockup-monitor.png` direction:

- Dark TodoX admin layout with gold accents.
- Summary cards for total, processing, completed, and failed jobs.
- Filters for date range, account, service, status, search, and sorting.
- Server-side paging with a default page size of 50.
- Job table with status, current stage, identifiers, and row actions.
- Detail drawer with Overview, Timeline, Input / Output, and Logs tabs.
- Quick impersonation entry point with audit logging.
- Administrator-only access through `AdminEndpointAuthorization.IsAdmin`.

## Changed Files

- `Components/Pages/AdminJobMonitor.razor`
- `Components/Pages/AdminJobMonitor.razor.css`
- `Models/AdminJobMonitorModels.cs`
- `Services/AdminJobMonitorService.cs`
- `Services/NavigationAccessRules.cs`
- `Components/Layout/MainLayout.razor`
- `Program.cs`
- `..\TodoX.Web.Tests\AdminJobMonitorTests.cs`

The concurrent RDance files and diagnostic document already present in the
working tree were preserved and are not part of this module's scope.

## Data and Security

The module reads existing render job, render event, service, account/customer,
and audit data. It does not add or modify migrations, tables, provider payloads,
credentials, or configuration secrets.

## Validation

- `dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-build --filter FullyQualifiedName~AdminJobMonitor`
  - Passed: 9, failed: 0.
- `dotnet build ..\TodoX.Dashboard.sln -c Release`
  - Passed: 0 errors, 46 existing warnings.
- `git diff --check`
  - Passed.
- Full test suite:
  - 899 passed, 13 failed.
  - The failures are existing regression tests in RDance, Timelapse, billing,
    favorite services, and related protected workflows; none target the new
    Admin Job Monitoring Dashboard tests.

## Publish

The dashboard publish command is:

```powershell
dotnet publish TodoX.Web.csproj -c Release --no-restore -o D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard
```

The publish output is intentionally kept under the repository's configured
`artifacts\publish\todox-dashboard` directory.

## Untouched Protected Areas

Image generation, video generation, voice/audio, point/billing contracts,
provider payloads, and database migrations were not changed for this feature.
