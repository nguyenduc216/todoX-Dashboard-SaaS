# RDance Create Flow Diagnostic

**Task:** `TDC-RDN-DIAGNOSTIC-CREATE-FLOW-20260909-003`  
**Date:** 2026-09-09  
**Scope:** Diagnostic only. No production code, API, database, migration, or render workflow changes were made.

## Executive Finding

The uploaded MP4 is saved successfully, but upload does not enqueue a render job. This is consistent with the current two-phase workflow:

1. Create or ensure a RDance draft.
2. Upload or stage the reference motion source.
3. Upload/prepare and approve the reference image.
4. Explicitly queue the render from the job detail page.

The missing step observed in the create-page path is the render enqueue trigger after upload. `OnMotionSelected` ends after `UploadMotionAsync` and navigation to the detail page. It does not call `QueueRenderAsync`.

This is not an API endpoint mismatch: upload and render are intentionally exposed as separate endpoints, and the upload endpoint does not enqueue a render job.

## Current Flow

### Create page

The current component is `Components/Pages/RDanceJobCreate.razor`. There is no `RDanceCreate.razor` component in the repository.

#### Draft creation

`CreateDraftAndOpenAsync` at `Components/Pages/RDanceJobCreate.razor:430-435` calls:

```text
RDanceFeatures.IsNewUiEnabled
  -> EnsureDraftAsync()
  -> CreateDraftAsync()
  -> DanceSell.CreateJobAsync(...)
  -> POST /api/dance-sell/jobs
  -> DanceSellPhase2Service.CreateJobAsync(...)
  -> repository CreateDraftAsync(...)
```

The create-page `Tạo video` button also points to `CreateDraftAndOpenAsync`. On this page it creates/opens the draft; it does not queue the render.

#### MP4 upload

The video input is wired to `OnMotionSelected` in the new UI. The method is at `Components/Pages/RDanceJobCreate.razor:512-535`:

```text
ValidateMotionVideo(...)
  -> EnsureDraftAsync() or CreateDraftAsync()
  -> read IBrowserFile bytes
  -> DanceSell.UploadMotionAsync(...)
  -> Navigation.NavigateTo("/jobs/rdance/{id}")
```

There is no call to `QueueRenderAsync` in this method.

### Upload service and endpoint

`Services/DanceSell/DanceSellPhase2Endpoints.cs` maps:

- `POST /api/dance-sell/jobs` at line 19: create draft.
- `POST /api/dance-sell/jobs/{id}/motion/upload` at line 29: upload motion.
- `POST /api/dance-sell/jobs/{id}/motion/tiktok` at line 30: stage TikTok source.
- `POST /api/dance-sell/jobs/{id}/reference/generate` at line 31: generate reference image.
- `POST /api/dance-sell/jobs/{id}/render` at line 35: queue render.

The upload endpoint handler at lines 188-189 calls only:

```text
service.UploadMotionAsync(...)
```

`DanceSellPhase2Service.UploadMotionAsync` at `Services/DanceSell/DanceSellPhase2Services.cs:1728-1736`:

1. Verifies ownership.
2. Saves the uploaded media through `SaveUploadedVideoAsync`.
3. Calculates duration.
4. Updates motion media/source fields in the RDance repository.
5. Returns the refreshed job DTO.

It does not call `_renderJobs.EnqueueAsync(...)`.

## Expected Flow

The expected render flow, based on the existing detail-page implementation, is:

```text
Create draft
  -> upload/stage motion
  -> upload character/product image or provide direct reference
  -> generate reference
  -> approve reference
  -> click Tạo video, or satisfy auto-finish conditions
  -> QueueRenderAsync
  -> POST /api/dance-sell/jobs/{id}/render
  -> DanceSellPhase2Service.QueueRenderAsync
  -> ValidateReadyForRender
  -> estimate/charge points and create operation
  -> RenderJobService.EnqueueAsync
  -> persist RDance render job id/status
  -> RenderJobWorker dispatches DanceSellRenderHandler
  -> provider submit/poll/completion flow
```

### Detail-page manual trigger

`Components/Pages/RDanceJobDetail.razor:1296-1334` contains `ConfirmAndQueueAsync`.

For manual mode, after readiness checks and confirmation, it:

1. Saves business fields with `UpdateBusinessAsync`.
2. Reloads the current job.
3. Verifies approved prepared reference.
4. Calls `DanceSell.QueueRenderAsync(...)` at line 1329.
5. Reloads the UI.

### Detail-page auto-finish trigger

`ContinueAutoFinishAsync` at `Components/Pages/RDanceJobDetail.razor:1554-1603` queues only when:

- auto-finish is enabled;
- the prepared reference is approved;
- `MotionVideoMediaId` exists;
- the job is not busy or already active.

When the reference is only `Ready`, auto-finish first approves it and returns. A later state refresh/continuation is required before the render queue call is reached.

## Missing Step and Fault Location

### Missing step

The create-page upload path has no transition from “motion source uploaded” to “render queued”:

```text
RDanceJobCreate.OnMotionSelected
  -> DanceSell.UploadMotionAsync
  -> NavigateTo(detail)
  -> STOP
```

The missing operation would be a deliberate render trigger after all required assets and reference approval are ready. Uploading the MP4 alone is insufficient because `ValidateReadyForRender` requires:

- valid character/direct reference;
- approved prepared reference and URL;
- ready motion media/source.

### Faulting code block

Primary block:

- File: `Components/Pages/RDanceJobCreate.razor`
- Method: `OnMotionSelected`
- Lines: `512-535`
- Relevant lines: `528-533`
- Behavior: upload and navigate only; no `QueueRenderAsync`.

Secondary UI behavior:

- File: `Components/Pages/RDanceJobCreate.razor`
- Method: `CreateDraftAndOpenAsync`
- Lines: `430-435`
- Behavior: the create-page `Tạo video` button creates/opens a draft and navigates; it is not the render queue action.

The actual queue trigger remains on the detail page:

- File: `Components/Pages/RDanceJobDetail.razor`
- Method: `ConfirmAndQueueAsync`
- Line: `1329`

## Historical Attribution

The current branch is `feature/rdn-onepage-ui-revamp` at `bc2a0a1`.

- `24fcd6b` (`feat(rdn): add one page workflow ui`, 2026-09-07) introduced the detail one-page workflow UI and feature configuration.
- `dd80a07` migrated the create page toward the one-page layout and moved source controls into the workflow cards.
- `24fab21` added the new UI flag usage, `EnsureDraftAsync`, image upload controls, and the current create-page one-page branch.
- In `24fab21`, the create-page upload path remained structurally:

```text
CreateDraftAsync/EnsureDraftAsync
  -> UploadMotionAsync
  -> NavigateTo(detail)
```

The historical pre-migration implementation also did not call `QueueRenderAsync` from create-page upload. Therefore the evidence supports a missing trigger/expectation in the create-page presentation flow, rather than a newly broken upload API or a backend render enqueue regression introduced by the one-page UI commit.

## Payload Audit

### Draft-create payload

`RDanceJobCreate.razor:548-566` creates `DanceSellCreateJobRequest` with:

- `Title`
- `Prompt`
- `PlacementMode`
- `ReferenceMode`
- `Mode`
- `Ratio`
- `CharacterOrientation`
- `AutoFinish`

`DanceSellPhase2Service.CreateJobAsync` at `Services/DanceSell/DanceSellPhase2Services.cs:1611-1644` adds authenticated `CustomerId` and `UserId` when persisting the draft. The create request does not contain image/video bytes because those are attached through separate upload/staging calls.

### Motion upload payload

`UploadMotionAsync` carries:

- job id;
- uploaded file bytes;
- file name;
- content type;
- authenticated user context.

It updates persisted motion media/source fields, but it does not send a render payload.

### Render payload

`DanceSellPhase2Service.QueueRenderAsync` at `Services/DanceSell/DanceSellPhase2Services.cs:1749-1885` builds the render job from persisted RDance state:

- input: `DanceSellJobId`, logical request id, operation id;
- prompt: prompt, placement mode, mode, orientation, ratio;
- references: approved prepared reference URL, motion video URL, operation id;
- provider code and model code;
- authenticated `UserId` and `CustomerId`;
- point estimate/status.

The model image and product image are represented by persisted job/reference state and are validated before render; they are not expected as raw fields in the initial create request.

## Render Job Creation and Handler Trace

1. `RDanceJobDetail.razor:1329` or `:1594` calls `DanceSell.QueueRenderAsync`.
2. `POST /api/dance-sell/jobs/{id}/render` is mapped by `DanceSellPhase2Endpoints` at lines 35 and 210-211.
3. `DanceSellPhase2Service.QueueRenderAsync` validates readiness, calculates pricing, handles operation/point state, and calls `_renderJobs.EnqueueAsync(...)` at lines 1868-1882.
4. `RenderJobService.EnqueueAsync` at `Services/Render/RenderJobService.cs:61-128` inserts the core render job and writes `JOB_QUEUED`.
5. `DanceSellRenderHandler` is registered in `Program.cs:243` and handles `RenderJobTypes.DanceSell`.
6. `DanceSellRenderHandler.HandleAsync` at `Services/DanceSell/DanceSellRenderHandler.cs:78-114` submits or polls the provider only after the render job already exists.

Conclusion: `DanceSellRenderHandler` cannot compensate for a missing enqueue call because it is downstream of render-job creation.

## Feature Flag Check

`appsettings.json` currently contains:

```json
"Features": {
  "EnableRDanceNewUI": true,
  "RdnOnePageUiEnabled": false
}
```

`Services/DanceSell/RDanceFeatureService.cs:14-16` resolves:

```text
Features:EnableRDanceNewUI
  ?? Features:RdnOnePageUiEnabled
```

Therefore the new RDance UI is enabled because `Features:EnableRDanceNewUI` is `true`. `Program.cs:221` registers `IRDanceFeatureService`, and both create/detail pages consume the service. No separate UI flag service or additional appsettings key was found in the inspected path.

## Recommended Fix

Recommendation only; no implementation was made in this diagnostic task:

1. Keep upload/staging separate from render enqueue.
2. Make the create-page `Tạo video` action either:
   - remain an explicit “create/open draft” action with a clearly separate render action on detail, or
   - orchestrate the same readiness, approval, confirmation, billing, and queue logic already used by detail.
3. Do not enqueue immediately after MP4 upload unless the required image/reference approval state is confirmed.
4. If the product requirement is auto-render after all assets are ready, reuse the existing `ContinueAutoFinishAsync` conditions and ensure it is invoked after upload, image preparation, and reference approval state changes.
5. Add a focused regression test that asserts the intended UI action invokes the render queue only after `ValidateReadyForRender` prerequisites are met.

No API contract change, backend change, database change, migration, billing change, points change, provider payload change, or render-handler change is recommended for this diagnostic conclusion.

## Changes Made

- Created: `docs/RDN_CREATE_FLOW_DIAGNOSTIC_20260909.md`
- Production code changed: No
- API changed: No
- Database/schema/migration changed: No
- Render workflow/pipeline changed: No
- Billing/points changed: No
