# TDC-RDN-FINAL-HARDENING-REPORT-20260910-001

## Scope

Final hardening for the RDance customer flow on branch
`feature/rdn-onepage-ui-revamp`.

The change is limited to:

- communicating the AutoFinish behavior accurately;
- preventing render queueing until the explicit customer action;
- requiring a valid persisted motion duration before render;
- preserving the existing service-specific pricing and duration flow;
- adding focused RDance regression coverage.

## Root Cause and Result

AutoFinish could previously be interpreted as automatically rendering after
reference preparation. The customer UI now states that AutoFinish prepares and
may approve the reference image, while the customer must press `Tạo video` to
start render.

The detail page render gate now requires all of the following:

- motion media ID and URL;
- motion source stage status `Ready`;
- motion duration greater than zero;
- prepared reference status `Approved`;
- prepared reference URL;
- no active render/job state.

The automatic upload, reload, polling, and reference-completion paths do not
queue a render. The explicit `ConfirmAndQueueAsync` action remains the render
entry point.

## Duration Resolution

`ResolveMotionDurationSecondsAsync` resolves duration in this order:

1. persisted job `request_json.durationSeconds`;
2. latest motion operation `request_json.durationSeconds`;
3. duration derived from the stored binary;
4. route configuration only when persisted values are absent.

Persisted zero or negative values do not fall through to route configuration.
An unresolved duration returns `DANCE_SELL_VIDEO_DURATION_REQUIRED`, which is
mapped to the customer-facing reload-video message.

## Changed Files

- `Components/Pages/RDanceJobCreate.razor`
  - Uses the shared AutoFinish wording that explicitly requires customer
    render confirmation.
- `Components/Pages/RDanceJobDetail.razor`
  - Adds the duration-not-ready warning.
  - Tightens `CanRender` with motion readiness and positive duration.
  - Prevents the AutoFinish text from promising automatic render.
  - Maps duration errors to the customer-facing message.
- `Services/DanceSell/DanceSellModels.cs`
  - Adds customer error mappings for missing/unavailable duration.
- `Services/DanceSell/DanceSellPhase2Services.cs`
  - Resolves and persists motion duration using the existing job/operation
    data flow and existing repository methods.
- `Tests/RDanceCustomerStatusAndPointsRegressionTests.cs`
  - Adds regression checks for the explicit render gate, AutoFinish wording,
    and duration precedence/persistence.
- `docs/TDC-RDN-FINAL-HARDENING-REPORT-20260910-001.md`
  - This report.

No new public API, endpoint, database field, migration, provider payload, or
render queue method was added.

## Validation

Focused RDance test:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~RDanceCustomerStatusAndPointsRegressionTests
PASS: 22 passed, 0 failed
```

Build:

```text
dotnet build TodoX.Web.csproj -c Release --no-restore
PASS: 0 errors
```

Publish:

```text
dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard
PASS: output written to artifacts/publish/todox-dashboard
```

Diff check:

```text
git diff --check
PASS
```

Formatting verification was checked earlier with
`dotnet format --verify-no-changes`; it reports pre-existing whitespace in
`DanceSellPhase2Services.cs` outside this task's diff. The file was not
formatted wholesale to avoid unrelated churn.

Full Phase1B test:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore
438 passed, 8 failed
```

The eight failures are outside this task's behavior: one Timelapse assertion,
four generic video prompt parser assertions, and three older RDance reference
prompt/retry source assertions. The focused RDance hardening suite passed
completely.

## Render Gate

- Upload auto render: `BLOCKED`
- Reload auto render: `BLOCKED`
- Poll auto render: `BLOCKED`
- Reference completion auto render: `BLOCKED`
- Explicit `Tạo video`: `ALLOWED` when the full gate is satisfied

## Duration

- Persisted: `YES`, using existing request JSON/repository persistence
- Required before render: `YES`
- Invalid duration blocked: `YES`

## Pricing

- Selected service respected: `YES`, existing service identity/pricing flow
  remains intact
- `FASHION_VIDEO` pricing preserved: `YES`
- Legacy fallback preserved: `YES`

The pricing implementation and service identity support were already present
in the branch before this working diff and were not modified by this task.

## Protected Areas Untouched

- 79AI/provider contracts and payloads
- render worker and provider execution pipeline
- image generation behavior
- video generation/provider integration
- billing and points calculation contracts
- database schema and migrations
- RVideo and Timelapse behavior
- upload and TikTok processing contracts

Runtime smoke verification against a live customer job was not available in
this validation session.

## Commit and Push

Required commit message:

```text
fix(rdance): harden manual render duration gate
```

Implementation commit hash: `715ba3a304df8e6a1377b4f8b441d74d0c5b39c4`

Push:

- branch: `feature/rdn-onepage-ui-revamp`
- result: `PASS`, pushed to
  `origin/feature/rdn-onepage-ui-revamp`
