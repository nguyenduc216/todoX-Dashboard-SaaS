# TDC-RDN-DURATION-UI-AUDIT-FIX-20260909-013

## Scope

Only `RDanceJobDetail.razor` New UI was changed:

- Display the existing RDance motion duration from the persisted job request, latest Motion operation request, or configured route fallback.
- Move the TikTok and Upload controls from the video frame overlay into the Step 2 card header.

Provider, upload, render, billing, points, API contracts, and database schema were not changed.

## Duration Audit

The verified flow is:

```text
DanceSellMotionDuration.TryGetBillableSeconds(content)
  -> DanceSellRepository.UpdateMotionUploadAsync / UpdateMotionTikTokAsync
  -> dance_sell_jobs.request_json.durationSeconds
  -> DanceSellJobDto.RequestJson
  -> RDanceJobDetail.razor
  -> FormatDurationLabel(MotionDurationSeconds)
```

During `QueueRenderAsync`, the same resolved duration is also written to:

```text
dance_sell_provider_operations.request_json.durationSeconds
  -> DanceSellProviderOperationDto.RequestJson
```

The defect was that the New UI read only `DanceSellJobDto.RequestJson`, while point estimation already used the duration resolver with route fallback. The UI now reads, in order:

1. `DanceSellJobDto.RequestJson`
2. latest Motion operation `RequestJson`
3. Motion provider route `ConfigJson`

No new duration estimate or provider metadata probe was introduced. If all three sources are absent, the UI continues to show `—`.

## UI Fix

TikTok and Upload remain the existing handlers:

- `OpenTikTokEditorAsync`
- `OnMotionSelected`

They are now rendered in the Step 2 header, using the existing 32px button classes and active/inactive state logic. The media frame no longer contains the controls.

## Validation

Commands:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~RDanceCustomerStatusAndPointsRegressionTests
dotnet build TodoX.Web.csproj -c Release --no-restore
dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard
```

Results:

- `git diff --check`: PASS
- `dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~RDanceCustomerStatusAndPointsRegressionTests`: PASS, 17/17
- `dotnet build TodoX.Web.csproj -c Release --no-restore`: PASS, 0 errors, 45 existing compiler warnings
- `dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`: PASS
- Publish output: `artifacts/publish/todox-dashboard`
