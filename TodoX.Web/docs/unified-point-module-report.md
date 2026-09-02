## Repository / Branch
- Repository: `TodoX-Dashboard-SaaS`
- Branch: `integration/rdance-on-construction-video-core`

## Base Commit
- `11ef5a32e1b050c353edf9a3d234967628b465a0`

## Final Commit SHA
- Recorded after the implementation commit is created.

## Authorization
- Permission service used: existing `CurrentUserSession.Can(...)` permission model.
- Enforced permissions: `point_config.view`, `point_config.manage`, `wallet.view_all`, `wallet.topup`, `wallet.adjust`, `wallet.refund`, `voucher.view`, `voucher.manage`, `service_point_override.manage`.
- Customer-owned reads are limited to the authenticated customer; customer accounts cannot administer rates, overrides, wallets, or vouchers.

## rVideo Full Usage
- Service id: resolved from the existing rVideo core job service linkage.
- Image count source: effective scene image source; only scenes without usable input are `AI_GENERATE`.
- Video seconds: sum of persisted scene durations.
- Voice count: count of scenes requiring external paid voice generation.
- Parent total: calculated by `PreRenderUsagePlan` and `PointPricingService`.
- Child charge behavior: parent-billed image batches set `SkipCustomerCharge`; explicit image rerenders use `USER_RERENDER` and a deterministic wallet reference.

## rDance Full Usage
- Service id source: existing `FixedTodoXServiceCatalog.RDance` catalog lookup.
- Image usage source: `DirectReference` is zero; generated reference/composite path is one.
- Video duration: resolved from persisted job/route configuration or provider estimate.
- Service override result: `PointPricingService` receives the resolved catalog service id, allowing service override before global fallback.
- Parent total: includes image and duration-driven video usage.

## SYSTEM_RETRY
Automatic provider/worker retry paths reuse the existing logical request and do not create an additional customer point debit. rDance retry metadata now uses the real service id and resolved duration while remaining free.

## USER_RERENDER
- Image: one additional image unit, charged before provider submission.
- Video: Timelapse rerender uses the exact persisted clip duration.
- Voice: no new external voice rerender path was present in this branch.
- Idempotency: deterministic SHA-256-derived rerender references are used for wallet debits.

## Tests
- `PointModuleRegressionTests`: 10 passed in the targeted run.

## Build
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: passed with pre-existing generated Razor nullable warnings.

## Publish
- Pending final validation.

## Git Push
- Pending final commit and push.

## Files Changed
- Authorization, wallet/billing services, rVideo image/video aggregation and rerender worker, rDance pricing aggregation, Timelapse rerender billing, regression tests, manual verification SQL, and this report.

## Remaining Limitations
- No database schema or migration was added or executed.
- No separate rVideo scene-video rerender method exists in the current UI/service path.
