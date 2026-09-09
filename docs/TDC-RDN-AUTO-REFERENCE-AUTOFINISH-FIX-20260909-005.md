# TDC-RDN-AUTO-REFERENCE-AUTOFINISH-FIX-20260909-005

## Root cause

The RDance Create page stopped after `UploadMotionAsync()` or TikTok staging and
navigated to the detail page without resolving the prepared reference. The
auto-finish guard requires an approved prepared reference, so
`ContinueAutoFinishAsync()` could not reach the existing render queue path.

## Files changed

- `TodoX.Web/Components/Pages/RDanceJobCreate.razor`
- `TodoX.Web/Components/Pages/RDanceJobDetail.razor`
- `TodoX.Web/Tests/RDanceCustomerStatusAndPointsRegressionTests.cs`
- `docs/TDC-RDN-AUTO-REFERENCE-AUTOFINISH-FIX-20260909-005.md`

## Workflow

Before:

1. Upload or stage motion video.
2. Persist the source.
3. Navigate to detail.
4. No automatic reference resolution occurred in Create.
5. The approved-reference prerequisite was not met, so no render job was queued.

After:

1. Upload or stage motion video.
2. Reload the persisted RDance job.
3. Verify `MotionVideoMediaId`, `MotionVideoUrl`, and
   `SourceStageStatus == Ready`.
4. Resolve the reference using existing reference services.
5. Navigate to detail, where the existing
   `ContinueAutoFinishAsync()` / `ConfirmAndQueueAsync()` /
   `DanceSell.QueueRenderAsync()` flow remains the render owner.

## Reference cases

### Character only

When a character image exists and no product image exists, Create calls the
existing `References.ApproveCharacterAsync()` flow. The character becomes the
prepared approved reference without requiring a manual click.

### Character and product

When both character and product images exist, Create calls the existing
`References.AutoPrepareAsync()` flow. Provider/reference generation remains
inside the existing service implementation.

### Direct reference

Direct-reference jobs preserve their existing behavior and skip the automatic
character/product reference resolution branch.

## Motion source flows

- MP4 upload reloads the persisted job, checks source readiness, resolves the
  reference, and then opens the detail page.
- TikTok staging reloads the persisted job and uses the same reference
  resolution path. Detail invokes `AutoPrepareReferenceAsync()` after staging
  completes, outside the busy operation scope.

## Duplicate-render protection

No new queue method was added. Create does not call `QueueRenderAsync`.
Detail continues to use `ContinueAutoFinishAsync()` and its existing guards for
busy state, terminal states, and existing render activity before calling
`DanceSell.QueueRenderAsync()`.

## Feature flag

The existing `IRDanceFeatureService` remains the source of truth:

- `Features:EnableRDanceNewUI`
- fallback: `Features:RdnOnePageUiEnabled`

No appsettings values were added or hard-coded.

## Validation

Commands run:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~RDanceCustomerStatusAndPointsRegressionTests
```

Result: PASS, 13 passed, 0 failed.

```text
dotnet build TodoX.Web.csproj -c Release --no-restore
```

Result: PASS, 0 errors. Existing generated Razor nullable warnings remain.

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore
```

Result: 414 passed, 9 failed. The failures are existing unrelated regression
assertions in Timelapse worker UI, RDance reference prompt, TodoX video prompt
parsing, and RVideo hotfix coverage. No production code in those protected
areas was changed for this task.

```text
dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard
```

Result: PASS. Output:
`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

`git diff --check`: PASS.

## Protected areas

API contract, database schema/migrations, render provider integration, video
generation pipeline, billing, points, and provider payloads were not changed.
Publish artifacts are local output only and are not included in the commit.

Commit hash and push result are reported in the completion message because the
commit hash is created after this report file is written.
