# TDC-RDN-MANUAL-RENDER-DURATION-SERVICE-PRICING-FIX-20260910-001

## Scope

This task covers the RDance upload/TikTok render gate, motion duration persistence,
and customer service-specific sell pricing. Provider contracts, render execution,
billing schema, points schema, database migrations, and provider integrations were
left unchanged.

## Root Causes

1. RDance detail auto-finish previously contained a render queue branch. Reload,
   polling, or reference completion could therefore reach `QueueRenderAsync`
   without the customer pressing the render button.
2. Motion duration is extracted from the uploaded/staged binary, but the durable
   source of truth must remain the existing `request_json.durationSeconds`.
   Queue and UI resolution now preserve that value and use operation/route values
   only as fallbacks.
3. Customer pricing was resolved from the generic RDance service identity instead
   of the selected commercial service. Jobs now carry `serviceId` and `serviceCode`
   in the existing request JSON snapshot, and pricing resolves the exact catalog
   service before calling `IServiceSellPriceResolver`.

## Flow Before / After

Before:

```text
upload or TikTok stage
  -> reload/reference auto-finish
  -> auto queue render (unintended)
```

After:

```text
upload or TikTok stage
  -> extract and persist duration
  -> prepare/approve reference when applicable
  -> remain draft/ready
  -> customer presses "Tao video"
  -> ConfirmAndQueueAsync
  -> QueueRenderFromUserActionAsync
  -> QueueRenderAsync
  -> existing render worker/provider flow
```

`ReloadAsync`, `OnParametersSetAsync`, `AutoPrepareReferenceAsync`,
`ContinueAutoFinishAsync`, and `PollLoopAsync` do not queue a render.
`ContinueAutoFinishAsync` is limited to reference approval.

## Duration Flow

```text
UploadMotionAsync / StageTikTokAsync
  -> DanceSellMotionDuration.TryGetBillableSeconds(content)
  -> DanceSellRepository.UpdateMotionUploadAsync /
     UpdateMotionTikTokAsync
  -> dance_sell_jobs.request_json.durationSeconds
  -> QueueRenderAsync operation request_json.durationSeconds
  -> RDanceJobDetail ResolveMotionDurationSeconds
  -> Duration UI
```

The lookup order is persisted job request, latest motion operation request, then
route configuration as the final fallback. Route configuration does not overwrite
an actual persisted duration.

## Service Identity and Pricing

The create page passes the selected `ServiceId` and `ServiceCode`. The RDance
service validates the catalog service is enabled and uses the RDance engine type,
then persists the identity in the existing request JSON. Customer pricing resolves
that identity and calls `IServiceSellPriceResolver.EstimateAsync`, which matches
the configured asset type, quality tier, and exact duration.

Jobs without service identity retain the legacy fallback to the existing RDance
point pricing path. No new database column or migration is required.

## Changed Files In This Working Commit

- `Components/Pages/RDanceJobCreate.razor`
- `Components/Pages/RDanceJobDetail.razor`
- `Services/DanceSell/DanceSellCustomerPricing.cs`
- `Services/DanceSell/DanceSellPhase2Services.cs`
- `Tests/RDanceCustomerStatusAndPointsRegressionTests.cs`

The supporting service identity, repository persistence, catalog lookup, DI
registration, and pricing implementation were already present in the branch
parent commit `f45e054` when this continuation started. Concurrent RVideo/Platform
changes were not staged or modified by this task.

## Regression Coverage

Focused command:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~RDanceCustomerStatusAndPointsRegressionTests
```

Result: PASS, 20/20.

The focused checks cover:

- upload/reference/polling paths cannot queue render;
- the explicit user action is the render gate;
- motion duration extraction and persistence;
- duration copied into the render operation request;
- selected service identity and legacy pricing fallback.

Full Phase1B command:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore
```

Result at validation time: 436 passed, 8 failed. The remaining failures are
pre-existing source/assertion regressions outside this task: Timelapse, generic
video prompt metadata, and older RDance reference-prompt/retry expectations.
They do not involve the five files changed in this commit's behavior.

## Build / Publish

Build command:

```text
dotnet build TodoX.Web.csproj -c Release --no-restore
```

Result: PASS, 0 errors. Existing generated Razor nullable warnings remain.

Publish command:

```text
dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard
```

Result: PASS. Output was written to
`artifacts/publish/todox-dashboard`.

## Protected Areas

Untouched:

- provider API contracts and 79AI payloads;
- render worker/provider execution pipeline;
- database schema and migrations;
- billing schema and point calculation contract;
- wallet, billing, refund, download, and result-video flows;
- RVideo/Platform concurrent work.

Runtime smoke verification against a live customer job was not available in this
validation session.

## Commit / Push

Commit message:

```text
fix(rdance): require explicit render and honor service pricing
```

Commit SHA before report finalization: `a79691d`.
The final amended SHA and push result will be recorded after the report update.
