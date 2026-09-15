# Admin Job Monitor UI Report

## Repository / Branch

Repository: `todoX-Dashboard-SaaS`  
Branch: `feature/rdn-onepage-ui-revamp`

## Base Commit

`cd8ea88` (`docs(rvideo): record per-job observation fix sha`)

## Final Commit SHA

See the final `git log -1` result reported with this change.

## Current UI Problems Found

- Filter controls did not present a stable desktop row.
- MudTable cells used independent browser table sizing, so header/body alignment was not guaranteed.
- Column separators were not consistently visible.
- Result thumbnails were too small and had no duration overlay.
- The job summary and drawer media needed stronger mockup proportions.
- Account filtering was not available in the filter data model.

## Mockup Comparison

The supplied mockup image and HTML reference were not present in the workspace attachments. The implementation follows the explicit visual contract in the task: existing TodoX shell, four summary cards, compact horizontal filters, shared data-grid columns, portrait media, and an overlay detail drawer.

## Razor/HTML Changes

- Added a compact account selector and retained date, service, status, sort, search, and refresh controls.
- Added duration overlay markup to result thumbnails.
- Kept thumbnail and row actions opening the read-only drawer.
- Preserved the four drawer tabs and quick-login action.

## CSS Changes

- Added scoped Job Monitor CSS variables and responsive rules.
- Increased result thumbnail dimensions while retaining `9 / 16`.
- Added shared vertical and horizontal grid separators.
- Tuned statistic card height, drawer media sizing, and desktop filter proportions.
- Kept global `body`, `html`, `button`, `table`, `.card`, and `.modal` styles untouched.

## Table Grid Alignment

The header and body rows now both use `var(--job-monitor-columns)`, with the same eight columns:

`Kết quả`, `Thời gian`, `Tài khoản`, `Dịch vụ`, `Trạng thái`, `Job ID`, `Giai đoạn hiện tại`, `Thao tác`.

## Filter Layout

Desktop filters use one grid row. Date inputs are grouped in one compact range control, followed by account, service, status, sort, search, and refresh. Narrow layouts wrap only below the responsive breakpoint.

## Thumbnail / Duration

Existing output metadata continues to provide the actual thumbnail/result URL. Duration is derived from existing `input_json` or `options` payload fields and displayed as `mm:ss` on the result thumbnail.

## Statistics Cards

Four cards remain in the required order and retain their icons, labels, counts, and existing TodoX color treatment.

## Detail Drawer

The drawer remains closed by default, overlays from the right, includes an explicit close button, keeps the four tabs, and stays read-only except for the existing quick-login action.

## Timeline

The timeline remains horizontal and continues to use existing event data, timestamps, status, and duration information.

## Input / Output

Existing input/output links and JSON payloads remain read-only and unchanged in behavior.

## Logs

Existing chronological logs remain read-only and scroll within the drawer content.

## Responsive Behavior

- Desktop: four cards, one-row filters, aligned grid, portrait thumbnails, overlay drawer.
- Narrow widths: filters and cards may wrap, while the table receives only the narrow-screen horizontal overflow required for readability.

## Database Changes

No migration or schema change was created or executed. Account options and duration use existing data.

## Tests

- `git diff --check`: passed.
- Focused Admin Job Monitor tests: `11 passed, 0 failed`.
- Full test suite: `942 passed, 20 failed`. The failures are outside this change in existing RDance, Timelapse, billing, favorites, and 79AI/provider tests.

## Build

`dotnet build TodoX.Dashboard.sln -c Release`: passed with `0 errors` and `46 warnings`. Warnings are existing generated Razor nullable warnings and one existing test warning.

## Publish

Command:

`dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`

Result: passed. Output:

`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

## Files Changed

- `TodoX.Web/Components/Pages/AdminJobMonitor.razor`
- `TodoX.Web/Components/Pages/AdminJobMonitor.razor.css`
- `TodoX.Web/Models/AdminJobMonitorModels.cs`
- `TodoX.Web/Services/AdminJobMonitorService.cs`
- `TodoX.Web.Tests/AdminJobMonitorTests.cs`
- `TodoX.Web/docs/reports/admin-job-monitor-ui-report.md`

## Known Limitations

- The reference image and HTML file were not available in the provided workspace attachments, so visual validation used the detailed task specification and existing TodoX shell conventions.
- Full-suite failures remain in unrelated protected subsystems and were not modified.
