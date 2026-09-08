# TDC-RDN-AUTO-RENDER-FLOW-FIX-20260909-004

## Scope

Fixed the RDance Create page transition after motion-video upload. The change is limited to UI state/readiness handling and reuses the existing Detail page auto-finish/render trigger.

Protected areas intentionally untouched:

- Database schema and migrations
- API contracts and endpoint definitions
- RDance render pipeline and provider integration
- Billing and point calculation
- Image-generation and motion-generation service contracts

## Files Changed

- `Components/Pages/RDanceJobCreate.razor`
- `Components/Pages/RDanceJobDetail.razor`
- `Tests/RDanceCustomerStatusAndPointsRegressionTests.cs`
- `docs/TDC-RDN-AUTO-RENDER-FLOW-FIX-20260909-004.md`

## Flow Before

The Create page motion upload handler was:

```text
OnMotionSelected
  -> EnsureDraftAsync/CreateDraftAsync
  -> UploadMotionAsync
  -> NavigateTo(/jobs/rdance/{id})
```

The handler did not reload the persisted job or verify that the complete render
readiness state had been reached. The upload itself does not enqueue a render.

## Flow After

The Create page motion upload handler now performs:

```text
OnMotionSelected
  -> EnsureDraftAsync/CreateDraftAsync
  -> UploadMotionAsync
  -> DanceSell.GetAsync(job.Id, currentUser)
  -> HasAutoFinishPrerequisites(job)
```

Readiness requires:

- `MotionVideoMediaId` exists
- `MotionVideoUrl` is present
- `SourceStageStatus == Ready`
- `PreparedReferenceUrl` is present
- `PreparedReferenceStatus == Approved`

When readiness is incomplete, the refreshed job remains on the Create page and
only the UI state is updated. No render request is sent.

When readiness is complete, Create navigates to the existing Detail page. The
Detail page reload lifecycle invokes the existing `ContinueAutoFinishAsync()`.
That method owns the existing `ConfirmAndQueueAsync()`/`QueueRenderAsync` path
and remains the only render-queue owner. No new `QueueRenderAsync` or duplicate
render logic was added to Create.

## UI Status Alignment

Create and Detail now use the same gray/yellow/green semantics:

- Gray: no data or failed/incomplete state
- Yellow: processing, staged, or awaiting completion/approval
- Green: completed state
- Render: yellow for queued/submitted/rendering and green for completed

The title remains in the header and is not rendered inside status step 4.

## Feature Flag

The RDance UI remains configuration-driven through:

```text
Features:EnableRDanceNewUI
```

with fallback to:

```text
Features:RdnOnePageUiEnabled
```

Current `appsettings.json` values are:

```json
"Features": {
  "EnableRDanceNewUI": true,
  "RdnOnePageUiEnabled": false
}
```

No feature flag was hard-coded in the component and no new configuration key
was added.

## Validation

Build:

```text
dotnet build TodoX.Web.csproj -c Release --no-restore
```

Result: **PASS**. The build completed with existing generated Razor nullable
warnings and 0 errors.

Relevant regression test:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TodoX.Web.Tests.RDanceCustomerStatusAndPointsRegressionTests"
```

Result: **PASS**, 13 passed, 0 failed.

Publish:

```text
dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard
```

Result: **PASS**.

Output:

```text
artifacts/publish/todox-dashboard
```

The broader `FullyQualifiedName~RDance` filter still contains three pre-existing
unrelated failures in `RDanceReferencePromptRegressionTests`; those failures
are outside this flow fix and were not changed.
