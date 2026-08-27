# Timelapse Service Split + Scene UX Report

## Git
Branch: `integration/rdance-on-construction-video-core`
Base SHA: `b410aad8ff7f2fed67d6859ecd601973262d3062`
Final SHA: `b410aad8ff7f2fed67d6859ecd601973262d3062` (no commit created)
Push: NOT PERFORMED

## Existing catalog audit
Current generic service: `CONSTRUCTION_VIDEO`
Catalog table: `catalog.services`
Service code: `CONSTRUCTION_VIDEO`
Visibility strategy: preserve for legacy jobs, set inactive in the manual SQL script.

## Profile audit
Profile table: `public.todox_timelapse_prompt_profiles`
Enabled categories: construction, living_room, bedroom, kitchen, pool, infrastructure, landscape (category values are used by the existing profile repository contract).
Profiles per category: queried at runtime by category; landscape seed evidence includes profiles 71/72/73.
Unmapped profiles: live database audit not completed; PostgreSQL CLI/driver tooling was unavailable in this environment.

## New services
1. Service code: `TIMELAPSE_CONSTRUCTION`; Display name: Timelapse Xây dựng công trình; Category: `construction`; Allowed profiles: enabled construction profiles; Default profile: first enabled category profile; Catalog visible: YES after SQL.
2. Service code: `TIMELAPSE_LIVING_ROOM`; Display name: Timelapse Phòng khách; Category: `living_room`; Allowed profiles: enabled living_room profiles; Default profile: first enabled category profile; Catalog visible: YES after SQL.
3. Service code: `TIMELAPSE_BEDROOM`; Display name: Timelapse Phòng ngủ; Category: `bedroom`; Allowed profiles: enabled bedroom profiles; Default profile: first enabled category profile; Catalog visible: YES after SQL.
4. Service code: `TIMELAPSE_KITCHEN`; Display name: Timelapse Nhà bếp; Category: `kitchen`; Allowed profiles: enabled kitchen profiles; Default profile: first enabled category profile; Catalog visible: YES after SQL.
5. Service code: `TIMELAPSE_POOL`; Display name: Timelapse Hồ bơi; Category: `pool`; Allowed profiles: enabled pool profiles; Default profile: first enabled category profile; Catalog visible: YES after SQL.
6. Service code: `TIMELAPSE_INFRASTRUCTURE`; Display name: Timelapse Cầu đường / Hạ tầng; Category: `infrastructure`; Allowed profiles: enabled infrastructure profiles; Default profile: first enabled category profile; Catalog visible: YES after SQL.
7. Service code: `TIMELAPSE_LANDSCAPE`; Display name: Timelapse Cảnh quan / Sân vườn / Cây xanh; Category: `landscape`; Allowed profiles: enabled landscape profiles; Default profile: first enabled category profile; Catalog visible: YES after SQL.

## Generic legacy service
Kept: YES
Visible: NO after manual SQL
Backward-compatible: YES; legacy route and jobs retain the legacy profile-only fallback.

## Server validation
Create validation: category-filtered enabled profile lookup.
Edit validation: category-filtered enabled profile lookup.
Start validation: snapshot category resolves the render profile by category; legacy snapshots use the existing fallback.
Worker/provider protection: construction bridge validates construction category before workflow dispatch; no provider routing changes.
Mismatch error code: `TIMELAPSE_PROFILE_SERVICE_MISMATCH`

## Scene pre-start UX
Pre-start detection: draft parent status, no active operations, zero generated attempts, and no final output.
Neutral text: `Chờ tạo video`
Spinner hidden: YES
Dependency text hidden: YES
Stop button hidden: YES

## Floating button
Label: `TẠO VIDEO`
Position: fixed middle-right of the viewport.
Visibility rule: pre-start, start-eligible, inactive, not busy, and not editing.
Action method: existing `StartOrResumeAsync` flow.
Mobile behavior: compact fixed action remains available; cards switch to one column.

## Card layout
Root cause: intrinsic grid sizing allowed rendering metadata to force narrow columns.
Desktop: 3 equal columns.
Tablet: 2 equal columns.
Mobile: 1 column.
Rendering card collapse fixed: YES.
Text wrap fixed: YES; cards use `min-width: 0`, `box-sizing: border-box`, normal word breaks, and break-word overflow.

## SQL
SQL update required: YES
File: `database/manual/timelapse/20260828_split_timelapse_services.sql`
Database: `todo_saas`
Idempotent: YES
Executed automatically: NO

## Schema
Migration required: NO

## Settings
appsettings.json update: NO
Other settings: NO
IIS/app recycle: NO

## Provider safety
79AI provider config changed: NO
YEScale touched: NO
YEScale MCP called: NO
YEScale config changed: NO
Fallback to YEScale added: NO
Other provider routing changed: NO

## Validation
Build: PASS - `dotnet build TodoX.Dashboard.sln -c Release --no-restore`
Focused tests: PASS - 137 passed, 0 failed.
Full tests: FAIL - 776 passed, 6 unrelated pre-existing RDance/fashion/billing failures.
git diff --check: PASS (only LF/CRLF conversion warnings).
Format: PASS for touched C# files with scoped `dotnet format`; full repository verification is blocked by pre-existing whitespace violations outside this change.
Publish: PASS - `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`.

## Acceptance
- [x] 7 Timelapse service definitions created
- [x] Each service locks profile category in UI and server validation
- [x] Single-profile UX auto-selects and uses read-only configuration display
- [x] Landscape category is isolated, with known profiles 71/72/73 from repository seed evidence
- [x] Server blocks mismatched profile
- [x] Legacy jobs retain compatibility fallback
- [x] Before start generated stages say `Chờ tạo video`
- [x] No spinner before start
- [x] No dependency wait text before start
- [x] Floating `TẠO VIDEO` is visible before start
- [x] Floating button uses canonical start flow
- [x] Scene cards use equal-width responsive columns
- [x] Text no longer breaks character-by-character
- [x] SQL requirement explicitly reported
- [x] appsettings requirement explicitly reported
- [x] Build passed
- [x] Focused tests passed
- [x] Publish passed
- [ ] Push completed (not performed)
