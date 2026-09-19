# RDance Video Flow Fix - 2026-09-17

## Status

Implementation, targeted validation, build, and publish completed. Production deployment and live post-deploy browser validation were not performed.

## Diagnostic Job

- Dance Sell job: `d4f7e068-9a07-45de-911c-72c193f80eb7`
- Render job: `d200bd0d-901f-407b-847b-4a9688b426f8`
- Provider task: `831ee0b1e059f6f5`
- Provider/model: existing `79ai` Kling Motion Control route
- Final Dance Sell state: `completed` / `completed`
- Final render state: `completed`
- Provider state: `SUCCESS`
- Result URL persisted: yes
- Poll count: `27`

The database was inspected with SELECT-only access. No database writes, migrations, or schema changes were made.

## Root Causes

### TikTok loading and resolve

The existing TikTok service already resolves and stages a direct MP4 through `DanceSell.StageTikTokAsync`. The UI set `_busy`, but did not render immediately before awaiting the long resolve/download operation. As a result, the loading state and disabled controls were normally not painted until the operation had already finished.

The UI now uses an explicit motion input state (`Resolving`, `Uploading`, `Ready`, `Failed`), invokes `StateHasChanged` before the awaited operation, and displays `Dang tai video...` with a progress indicator over the full media frame. Failures clear the active state, show an error, and allow retry.

### Reference/motion upload loading

Motion upload had the same render-timing issue: `_busy` was set before the upload, but the component did not yield a rendered loading state. The media frame now displays `Dang tai video len...`, the selected filename, and a progress indicator immediately. Existing validation, upload service, size limits, duration persistence, and duplicate-action guards remain in use.

### Blank result panel

The one-page result frame delegated the not-created state to a generic empty media frame. This gave the user no useful context and could appear blank inside the dark result container.

The not-created state now shows the current prepared/character reference image, current prompt, motion readiness, and a `Tao video` command. The command calls the existing `ConfirmAndQueueAsync` path, which retains current validation, estimate/confirmation, `QueueRenderAsync`, and render job creation. No second render endpoint or workflow was introduced.

### 79AI result not updating without browser reload

Backend persistence was not the failure for the diagnostic job. The observed chain was:

```text
79AI SUCCESS with result URL
  -> DanceSellCompletionService persisted result URL
  -> dance_sell_jobs = completed
  -> render_jobs = completed
  -> completed render events persisted
```

The frontend polling loop depended directly on the current in-memory `IsActive` value. A transient detail refresh failure cleared `_job`; that made `IsActive` false and prevented every later polling tick from issuing another reload. The backend could then complete normally while the stale page continued showing rendering until a browser reload.

Polling now:

- runs every 3 seconds while a render is being monitored;
- remembers that it is monitoring an active render across transient refresh failures;
- continues after a polling exception and logs a warning;
- accepts terminal state only after a successful job load;
- refreshes the component model and result URL through `ReloadAsync`/`StateHasChanged`;
- stops on a successfully loaded terminal state;
- restarts immediately after the existing queue action;
- cancels and disposes its timer/token when the component is disposed;
- never calls `QueueRenderAsync`.

## Fresh Motion Path

The diagnostic job used a fresh uploaded source MP4. Evidence showed provider reference upload/verify, motion provider upload identity, provider submit, repeated provider polling, provider success, result persistence, and both internal jobs completing.

The first provider operation failed at submit with a provider media error. The existing retry created a new operation, reused the verified motion asset, submitted provider task `831ee0b1e059f6f5`, and completed successfully. This task did not alter provider model policy, endpoint selection, request fields, retry policy, or asset reuse behavior.

## Files Changed

- `Components/Pages/RDanceJobCreate.razor`
- `Components/Pages/RDanceJobDetail.razor`
- `Tests/RDanceVideoFlowRegressionTests.cs`
- `docs/reports/rdance-video-flow-fix-2026-09-17.md`
- `Services/AiProviders/Ai79TaskClient.cs`: compile-only local variable renames inside the pre-existing multipart change; no provider behavior or payload change.

Diagnostic artifacts outside the application source:

- `artifacts/codex/rdance-video-flow-fix-2026-09-17/evidence-TodoXSaaS.json`
- `artifacts/tmp/rdance-db-read/Program.cs`

## Tests

Targeted flow and customer regression tests:

```text
dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RDanceVideoFlowRegressionTests|FullyQualifiedName~RDanceCustomerStatusAndPointsRegressionTests"
PASS: 38 passed, 0 failed
```

Broader RDance suite:

```text
dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RDance"
PARTIAL: 61 passed, 3 failed
```

The three failures are pre-existing static assertions in `RDanceReferencePromptRegressionTests` for older reference/retry implementation text. They are unrelated to the loading, empty-state, and polling changes in this task.

## Build And Lint

```text
dotnet build TodoX.Web.csproj -c Release --no-restore
PASS: 0 errors, 45 existing generated Razor nullable warnings
```

```text
dotnet format TodoX.Web.csproj --verify-no-changes --no-restore
FAIL: repository-wide pre-existing whitespace findings across unrelated files
```

`git diff --check` passed. Unrelated formatting findings were not modified.

## Publish

```text
dotnet publish TodoX.Web.csproj -c Release --no-restore -o D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard
PASS
```

Verified output: `artifacts/publish/todox-dashboard/TodoX.Web.dll`.

## Deploy And Browser Validation

- Production deploy: not performed.
- IIS restart: not performed.
- In-app browser validation: unavailable because no browser connection was available.
- Local full application start was intentionally not used because current startup registers production-facing hosted workers and the repository configuration points at live services. Starting it locally without an isolated configuration could claim production work.
- Responsive behavior is covered by stable aspect-ratio containers and the existing responsive grid; live screenshots at the four requested viewports remain a post-deploy validation item.

## Protected Areas Untouched

- No database/schema/migration change.
- No image-generation behavior change.
- No voice/audio change.
- No point, wallet, billing, or pricing behavior change.
- No public API change.
- No 79AI model/provider policy change.
- No new provider workflow.
- No production configuration or credential change.

## Remaining

- Deploy the published artifact through the approved IIS workflow, then validate the job page at `1920x1080`, `1366x768`, `1024x768`, and `390x844`.
- Confirm on a new live render that the page transitions from queued/rendering to completed within one 3-second polling interval without browser reload.
- Repository-wide lint and the three existing RDance reference-prompt assertions remain outside this task.
