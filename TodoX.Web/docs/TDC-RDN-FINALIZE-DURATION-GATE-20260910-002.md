# TDC-RDN-FINALIZE-DURATION-GATE-20260910-002

## Scope

Final RDance duration contract and render-gate hardening on
`feature/rdn-onepage-ui-revamp`.

This task does not change UI layout, provider contracts, provider payloads,
image generation, reference-generation behavior, pricing, billing, points,
RVideo, Timelapse, database schema, migrations, or public endpoints.

Runtime/customer smoke test was not performed by Codex.

## Root Cause

The previous hardening still retained a route configuration fallback for
duration. A route default could therefore act as a substitute when the job,
operation, and binary did not provide an actual duration.

The detail UI also used null-coalescing over raw integer values, so a persisted
zero or negative value could prevent a valid operation duration from being
considered and was not expressed consistently as an invalid duration.

## Duration Contract

The final resolution order is:

1. job `request_json.durationSeconds` and supported aliases, only when `> 0`;
2. latest motion operation `request_json.durationSeconds` and supported aliases,
   only when `> 0`;
3. duration derived from the stored motion binary, only when `> 0`;
4. otherwise fail with `DANCE_SELL_VIDEO_DURATION_REQUIRED`.

Route configuration is no longer a duration source. It cannot provide a
synthetic duration for a new or incomplete video.

Duration obtained from the operation or binary is persisted through the
existing `PersistMotionDurationAsync` repository method. Upload and TikTok
staging continue to persist duration through the existing motion update
methods.

## Invalid Duration Behavior

Job duration values of `0` or less are invalid. Operation duration values of
`0` or less are invalid. Binary duration values that are missing, zero, or
negative are invalid.

Invalid values do not enable render and do not fall through to route
configuration. If no later valid actual source exists, the backend throws
`DANCE_SELL_VIDEO_DURATION_REQUIRED`.

The customer-facing mapping remains:

`Chua xac dinh duoc thoi luong video. Vui long tai lai video.`

## Render Gate

The backend `QueueRenderAsync` path validates the existing motion/reference
requirements and then resolves duration before pricing, charging, operation
creation, or render enqueue. A direct backend call cannot bypass the duration
gate.

Render requires:

- valid motion media ID;
- valid motion URL;
- `SourceStageStatus == Ready`;
- actual duration `> 0`;
- approved prepared reference;
- valid prepared reference URL;
- no active job/render state;
- all existing direct-reference and customer/job validations.

The UI `CanRender` gate remains aligned with these conditions. The UI duration
resolver accepts only positive job or operation durations and does not read
route configuration.

## AutoFinish

AutoFinish may prepare and approve the reference according to the existing
flow. It does not queue render. Upload, reload, polling, page initialization,
reference preparation, reference completion, and reference approval do not
queue render.

Only the explicit customer action `Tao video` reaches
`QueueRenderFromUserActionAsync` and the backend `QueueRenderAsync` entry point.

## Pricing

Pricing and service identity were not changed. Valid duration continues to be
passed into the existing customer pricing flow. `FASHION_VIDEO` pricing and
legacy fallback behavior remain unchanged.

## Changed Files

- `Components/Pages/RDanceJobDetail.razor`
  - removed route duration fallback from UI;
  - ignored non-positive persisted values;
  - kept job -> operation precedence.
- `Services/DanceSell/DanceSellPhase2Services.cs`
  - removed route duration fallback from backend;
  - kept job -> operation -> binary resolution;
  - simplified resolver parameters;
  - preserved existing persistence and render queue flow.
- `Tests/RDanceCustomerStatusAndPointsRegressionTests.cs`
  - updated source assertions for the positive-only UI resolver;
  - added coverage that zero/negative values and route defaults cannot bypass
    the duration gate.
- `docs/TDC-RDN-FINALIZE-DURATION-GATE-20260910-002.md`
  - this report.

## Regression Tests

Focused command:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~RDanceCustomerStatusAndPointsRegressionTests
PASS: 23 passed, 0 failed
```

Coverage includes:

- required render prerequisites;
- positive duration checks;
- job -> operation -> binary precedence;
- persistence from operation and binary;
- invalid persisted duration handling;
- no route-default bypass;
- customer duration error mapping;
- AutoFinish without automatic render.

## Validation

Build:

```text
dotnet build TodoX.Web.csproj -c Release --no-restore
PASS: 0 errors
```

Publish:

```text
dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard
PASS: output created at artifacts/publish/todox-dashboard
```

The publish output contains `TodoX.Web.dll`. Publish artifacts were not staged.

Diff check:

```text
git diff --check
PASS
```

Format verification:

```text
dotnet format --verify-no-changes --no-restore
NOT PASS: reports pre-existing whitespace violations across unrelated modules.
No wholesale formatting was applied.
```

Full Phase1B:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore
439 passed, 8 failed
```

The eight failures are outside this task: Timelapse, generic video prompt
parser assertions, and older RDance reference-prompt/retry assertions. The
focused RDance duration suite passes completely.

## Protected Areas

Untouched:

- 79AI/provider contracts;
- provider payloads;
- image generation;
- video provider execution;
- reference-generation behavior outside the duration gate;
- billing and points contracts;
- database schema and migrations;
- RVideo;
- Timelapse;
- upload and TikTok contracts;
- pricing and service identity.

## Commit

Required commit message:

```text
fix(rdance): finalize duration render gate
```

Commit hash: `6d6b62ebf9b7dea8047ab284e07a5940bd013376`

## Push

- branch: `feature/rdn-onepage-ui-revamp`
- remote: `origin/feature/rdn-onepage-ui-revamp`
- result: `PASS`

## Runtime Smoke Test

Not performed by Codex. Runtime/customer smoke test will be performed
separately by the owner.
