## Repository / Branch
- Repository: `TodoX-Dashboard-SaaS`
- Branch: `integration/rdance-on-construction-video-core`
- Base commit: `d4dc8c8f39df00dfeaec3678bd1646b712ca11bd`

## Summary
- Timelapse rerender now uses a fresh operation id when `Rerender` is requested, preserving idempotency for non-rerender edits.
- The point balance notifier is now singleton-scoped so balance refresh state is shared correctly.
- rVideo initial billing now charges the parent logical job once, snapshots the estimate, and prevents double billing on child image, video, and audio work.
- rDance staged billing keeps the displayed logical total at `IMAGE + VIDEO + VOICE` while only charging the remaining amount after the reference image has already been billed.

## rVideo Billing
- Parent image-batch billing is performed once before child work is enqueued.
- The parent charge uses the estimated image, video, and voice usage for the batch scenes.
- If the account has insufficient points, the parent job is marked failed and no child work is billed.
- Child image, video, and audio handlers skip customer charging when the parent bill is already recorded.

## rDance Billing
- `DIRECT_REFERENCE` still does not charge an AI reference image.
- AI reference generation charges one deterministic image unit before provider submission.
- Queueing renders keeps the logical total stable and only charges the remaining amount after the reference image debit.

## Point Permissions

| Permission | Exists | Role Assignment | Verification |
|---|---|---|---|
| `point_config.view` | Seeded idempotently | `support`, `admin`, existing system aliases | `verify_point_module.sql` |
| `point_config.manage` | Seeded idempotently | `support`, `admin`, existing system aliases | `verify_point_module.sql` |
| `wallet.view_all` | Seeded idempotently | `support`, `admin`, existing system aliases | `verify_point_module.sql` |
| `wallet.topup` | Seeded idempotently | `support`, `admin`, existing system aliases | `verify_point_module.sql` |
| `wallet.adjust` | Seeded idempotently | `support`, `admin`, existing system aliases | `verify_point_module.sql` |
| `wallet.refund` | Seeded idempotently | `support`, `admin`, existing system aliases | `verify_point_module.sql` |
| `voucher.view` | Seeded idempotently | `support`, `admin`, existing system aliases | `verify_point_module.sql` |
| `voucher.manage` | Seeded idempotently | `support`, `admin`, existing system aliases | `verify_point_module.sql` |
| `service_point_override.manage` | Seeded idempotently | `support`, `admin`, existing system aliases | `verify_point_module.sql` |

The migration resolves role codes instead of hard-coded ids and does not assign point-admin permissions to customer roles. Root users retain the existing code-level wildcard behavior.

## rVideo USER_RERENDER
- Image: existing `USER_RERENDER` path charges one deterministic IMAGE unit and remains idempotent.
- Video: no separate customer-facing scene-video `USER_RERENDER` or “render again” service exists in this branch. Current scene-video retry/enqueue actions are lifecycle/system retry behavior, not customer rerender billing.
- Result: scene-video `USER_RERENDER` is N/A in the current branch; no new UI feature was added.

## Tests
- Targeted point-module regression tests: 45 passed.
- Full test suite: 868 passed, 5 unrelated pre-existing failures.
- `git diff --check`: passed.

## Build
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: passed.
- Build emitted existing generated Razor nullable warnings.

## Publish
- `dotnet publish TodoX.Web\\TodoX.Web.csproj -c Release --no-restore -o artifacts\\publish\\todox-dashboard`: passed.
- Output: `artifacts/publish/todox-dashboard`

## SQL
- No database migrations were created, modified, or executed for this task.

## Files Changed
- `TodoX.Web/Components/Pages/TimelapseJobDetail.razor`
- `TodoX.Web/Program.cs`
- `TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs`
- `TodoX.Web/Services/Render/RenderJobService.cs`
- `TodoX.Web/Services/Render/SceneImageBatchRenderHandler.cs`
- `TodoX.Web/Services/Render/SceneImageRenderWorkItemHandler.cs`
- `TodoX.Web/Services/VideoRender/RVideoSceneAudioAutoChainService.cs`
- `TodoX.Web/Services/VideoRender/SceneVideoRenderHandler.cs`
- `TodoX.Web.Tests/DanceSellRenderHandlerTests.cs`
- `TodoX.Web.Tests/PointModuleRegressionTests.cs`
- `TodoX.Web.Tests/RDanceStagedBillingRegressionTests.cs`
- `docs/unified-point-module-report.md`

## Git
- Changes were committed and pushed on `integration/rdance-on-construction-video-core`.
