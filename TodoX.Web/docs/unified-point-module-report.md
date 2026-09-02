## Repository / Branch
- Repository: `TodoX-Dashboard-SaaS`
- Branch: `integration/rdance-on-construction-video-core`

## Base Commit
- `d4dc8c8f39df00dfeaec3678bd1646b712ca11bd`

## Final Pushed Commit SHA
- Implementation commit: `a7f9681`
- Documentation commit: `9567b77`
- Final pushed branch state recorded here: `9567b77`

## rDance Reference Billing
- `DIRECT_REFERENCE` does not generate or charge an AI reference image.
- AI reference generation resolves the `rDance` catalog service id and Standard/Premium image quality, estimates exactly one image unit, and charges the customer before `provider.SubmitAsync`.
- Insufficient balance raises `INSUFFICIENT_POINTS`; provider submission is not reached.
- Billing is stored on the existing provider operation, including estimate, charge, balances, billing status, and component snapshot.
- The image charge reference is deterministic from dance job id, `reference_image`, `initial_render`, and reference version.
- The per-job generation lock prevents concurrent duplicate submissions. Provider/system retries do not create another customer image debit.

## rDance QueueRender
- The displayed logical total remains `IMAGE + VIDEO + VOICE`.
- `QueueRenderAsync` reads the charged reference operation and subtracts its charged IMAGE points.
- Only the remaining job amount is charged with the existing `dance_sell_job` wallet reference.
- The persisted snapshot records planned and charged image/video/voice components, total planned points, total charged points, and remaining points.
- Direct reference jobs keep `image_count = 0`.

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
- Targeted staged-billing regression tests: 4 passed.
- Full test suite: 859 passed, 5 unrelated pre-existing failures.
- `git diff --check`: passed.

## Build
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: passed.
- Build emitted existing generated Razor nullable warnings.

## Publish
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`: passed.
- Output: `artifacts\publish\todox-dashboard`

## SQL
- Added idempotent permission seed: `database/migrations/20260902_point_module_permissions.sql`.
- Updated verification queries: `database/manual/verify_point_module.sql`.
- No migration was executed and no production database was changed.

## Git Push
- Implementation commit `a7f9681`: pushed successfully.
- Documentation commit `9567b77`: pushed successfully.

## Files Changed
- `TodoX.Web/Services/DanceSell/DanceSellAiOperations.cs`
- `TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs`
- `TodoX.Web/database/migrations/20260902_point_module_permissions.sql`
- `TodoX.Web/database/manual/verify_point_module.sql`
- `TodoX.Web.Tests/DanceSellRenderHandlerTests.cs`
- `TodoX.Web.Tests/RDanceStagedBillingRegressionTests.cs`
- `TodoX.Web/docs/unified-point-module-report.md`

## Remaining Limitations
- Database SQL remains manual by design.
- The five full-suite failures are pre-existing and outside this task's scope.
- `dotnet format --verify-no-changes` remains blocked by widespread pre-existing whitespace diagnostics outside the changed files.
