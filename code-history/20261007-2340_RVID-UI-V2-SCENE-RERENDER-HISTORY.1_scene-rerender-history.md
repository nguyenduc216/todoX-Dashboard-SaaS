# RVID-UI-V2-SCENE-RERENDER-HISTORY.1 — V2 per-scene manual re-render + media version history + explicit re-merge

CHANGE_ID: RVID-UI-V2-SCENE-RERENDER-HISTORY.1
Date: 2026-10-07
Agent: Claude (AI coding agent)
Branch: fix/prompt-assistant-tts-validation

## Requested scope

Complete the RVideo **V2** manual re-render / history workflow:

1. Prompt edit -> dirty-aware **"Tạo lại ảnh"** / **"Tạo lại video"** buttons (enabled only when the
   working prompt differs from the ACTIVE media version, or when the source image changed under an
   existing video).
2. Per-scene **media version history** (separate image vs video lists) with selecting an
   **active version**.
3. Explicit **"Ghép lại video"** re-merge action in the Result tab.

Reuse canonical V1 generation / billing / merge flows. No new provider payload, no new pricing rule.

## Allowed files / behaviors

- `TodoX.Web/Services/VideoRender/RVideoSceneImageRenderAction.cs` (new) — image re-render action.
- `TodoX.Web/Services/VideoRender/RVideoSceneVideoRenderAction.cs` (new) — video re-render action.
- `TodoX.Web/Services/VideoRender/RVideoSceneRerenderDirtyPolicy.cs` (new) — pure dirty policy.
- `TodoX.Web/Components/Dialogs/MediaHistoryDialog.razor` + `MediaHistoryItem.cs` (new) — history UI.
- `TodoX.Web/Components/Pages/RVideo/RVideoV2Preview.razor` — buttons + history + selection wiring.
- `TodoX.Web/Components/Pages/RVideo/RVideoV2Result.razor` — re-merge button + flow.
- `TodoX.Web/Components/Pages/RVideo/RVideoV2Shell.razor` — pass reload callback to Result.
- `TodoX.Web/Program.cs` — 2 scoped DI registrations.
- `TodoX.Web.Tests/RVideoV2SceneRerenderHistoryTests.cs` (new) — focused coverage.
- this history file.

## Out of scope / frozen areas — explicitly NOT touched

- Provider payloads / API contracts, pricing / charging rules: unchanged.
- VOICE / AUDIO: unchanged.
- Polling, render workers, lifecycle, merge engine internals: unchanged (re-used only).
- IMAGE GENERATION / VIDEO GENERATION provider adapters (RDance/DanceSell/79Ai), Timelapse: unchanged.
- POINT / BILLING (StaticImageBillingPolicy, RDance staged billing): unchanged.
- LEGACY UI (RenderVideoJobs.razor V1) and other frozen V2 tabs: unchanged.
- No DB migration / schema change / SQL executed.
- No commit / push / deploy (task says DO NOT).

## Changes

### Canonical re-render actions (UI-facing, thin)

- `RVideoSceneImageRenderAction.EnqueueRerenderAsync` — cost preflight via the canonical
  `IRVideoInitialPointEstimateService` (single-image rate `estimate.ImageRate`, NOT a hardcoded 0),
  `INSUFFICIENT_POINTS` guard, `HasActiveImageVersionAsync` dedup, creates a **queued immutable image
  version** through `ISceneMediaVersioningService.CreateQueuedImageVersionAsync`, then enqueues the
  canonical `SceneImageRenderWorkItemHandler.JobTypeName` work item. Stores `customerPointRate` /
  `customerPointQuality` in the version config snapshot.
- `RVideoSceneVideoRenderAction.EnqueueRerenderAsync` — duration x `VideoRate` estimate, re-uses
  `IVideoRenderEligibilityService.GetVideoRenderEligibilityAsync` + `RVideoVideoModelPolicy`,
  dedups via `EnqueueForSceneIfNoneActiveAsync`, applies the shared reference, enqueues the canonical
  `SceneVideoRenderHandler.JobTypeName`.
- Both are thin UI-facing wrappers over existing canonical services — no second render/billing path.

### Pure dirty policy (testable, shared)

- `RVideoSceneRerenderDirtyPolicy` — pure static policy used by the razor AND the tests:
  - image: enabled when no baseline (first gen) OR working prompt != active image snapshot;
  - video: enabled when working prompt != active video snapshot, OR the active image version changed
    since the active video was rendered (image→video dependency), OR no active video but a source
    image exists.
- Line endings normalised (`RVideoPromptBaseline`) — CRLF/LF never yields a false dirty state.
- Baseline is defined against the **active media version snapshot**, not the just-saved draft, so
  **Save alone does not clear dirty**.

### V2 Preview wiring

- Injects `IRVideoSceneImageRenderAction` / `IRVideoSceneVideoRenderAction` / `ISceneMediaVersioningService`.
- Dirty-aware `CanRerenderImage` / `CanRerenderVideo` delegate to the pure policy.
- `HandleRerenderImageAsync` / `HandleRerenderVideoAsync`: **SaveDraftOnlyAsync first**, confirm dialog,
  `_gate` single-flight dedup, per-scene `_rerenderingImage` / `_rerenderingVideo` sets, reload after.
- `HandleContentChangedAsync` only saves a draft — never auto-renders on typing.
- History dialogs `OpenImageHistoryAsync` / `OpenVideoHistoryAsync` build `MediaHistoryItem` lists via
  `ListImageVersionsAsync` / `ListSceneVideoVersionsAsync` (strict type separation: `IsVideo` false/true),
  only `completed` versions `CanSelect`.
- Version selection `SelectImageVersionAsync` / `SelectVideoVersionAsync` call canonical
  `SelectImageVersionAsync` / `SelectSceneVideoVersionAsync` with a draft-loss confirm dialog.
- `RefreshActiveVersionsAsync` hooked into `OnAfterRenderAsync` refreshes the active-version caches.
- Buttons added to desktop menu + mobile row.

### V2 Result re-merge

- Injects `IRVideoProjectFinalizationService` + `Snackbar` + `AuthStateService`.
- **"Ghép lại video"** button -> `ReMergeAsync` calls the canonical
  `TryEnqueueFinalMergeAsync(Project.Id, RVideoProjectFinalizationContracts.TriggerManual)`;
  surfaces `_merging` loading, `AlreadyActive` (info), `!Enqueued` with `already_final` vs
  `not_ready` messages, and Success; reloads via `OnReloadRequested`.
- Shell passes `OnReloadRequested="ReloadAsync"` to Result.

### DI

- `Program.cs`: `AddScoped<IRVideoSceneImageRenderAction, RVideoSceneImageRenderAction>()` and
  `AddScoped<IRVideoSceneVideoRenderAction, RVideoSceneVideoRenderAction>()` after the
  `IRVideoSceneVideoRecoveryService` registration.
## Canonical flows re-used (no second implementation)

- Image version -> `ISceneMediaVersioningService.CreateQueuedImageVersionAsync/GetSelectedImageVersionAsync`
- Video version -> `.../EnqueueForSceneIfNoneActiveAsync/GetSelectedVideoVersionAsync`
- Image/video enqueue -> `SceneImageRenderWorkItemHandler` / `SceneVideoRenderHandler` job types
- Eligibility -> `IVideoRenderEligibilityService.GetVideoRenderEligibilityAsync` + `RVideoVideoModelPolicy`
- Cost preflight -> `IRVideoInitialPointEstimateService` (existing, unchanged)
- Merge -> `IRVideoProjectFinalizationService.TryEnqueueFinalMergeAsync` (existing, unchanged)

## Files changed

- `TodoX.Web/Services/VideoRender/RVideoSceneImageRenderAction.cs` (new)
- `TodoX.Web/Services/VideoRender/RVideoSceneVideoRenderAction.cs` (new)
- `TodoX.Web/Services/VideoRender/RVideoSceneRerenderDirtyPolicy.cs` (new)
- `TodoX.Web/Components/Dialogs/MediaHistoryDialog.razor` (new)
- `TodoX.Web/Components/Dialogs/MediaHistoryItem.cs` (new)
- `TodoX.Web/Components/Pages/RVideo/RVideoV2Preview.razor`
- `TodoX.Web/Components/Pages/RVideo/RVideoV2Result.razor`
- `TodoX.Web/Components/Pages/RVideo/RVideoV2Shell.razor`
- `TodoX.Web/Program.cs`
- `TodoX.Web.Tests/RVideoV2SceneRerenderHistoryTests.cs` (new)
- this history file.

## Verification

- `dotnet build TodoX.Web/TodoX.Web.csproj -c Release` -> **Build succeeded, 0 errors**.
- Focused `FullyQualifiedName~RVideoV2SceneRerenderHistoryTests` -> **25 passed / 0 failed**.
- `FullyQualifiedName~RVideoV2` (Preview load + Shell + this) -> **117 passed / 0 failed**.
- Full suite `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release`
  -> **1254 passed / 21 failed / 1275 total**. All 21 are pre-existing failures in frozen/protected
  areas (Ai79TaskClient, RDanceFashionDemo, DanceSell, Timelapse, CorePlatformLifecycle,
  FavoriteServices, RDanceStagedBilling, StaticImageBillingPolicy). **None reference RVideo V2.**
- Baseline proof: with the 4 modified files stashed, the same failures reproduce for a representative
  subset (StaticImageBillingPolicy.RVideoInitialEstimateWires..., TimelapsePhase2A,
  FavoriteServices) -> 3 failed / 0 passed. So none are caused by this change.
- Publish `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release -o artifacts/publish/todox-dashboard`
  -> `artifacts/publish/todox-dashboard/TodoX.Web.dll` exists; 39 CSS/JS assets present under wwwroot.

## Runtime / visual verification

- **NOT RUNTIME VERIFIED** — no running app/browser/DB session in this task. No provider call was made
  and no paid job was enqueued. Structural + build + publish evidence only.
- **NOT VISUALLY VERIFIED** (desktop/mobile). Button enable/disable, history dialog, selection and
  re-merge snacks are asserted by source-contract tests, not observed live.

## Frozen subsystems / DB

- Billing/RDance/DanceSell/79Ai/Voice/Timelapse/Finalizer/Merge internals/provider-render-polling-lifecycle:
  UNTOUCHED. Legacy V1 UI preserved. No DB migration/schema/SQL executed.

## Git / deploy

- Commit SHA: N/A. Commit/Push/Deploy/Restart: **NOT EXECUTED** (per task DO NOT).

## Limitations / remaining blockers

- Live end-to-end (queue -> provider -> version -> merge) verification outstanding (needs a running
  instance + credentials).
- `RVideoSceneRerenderDirtyPolicy` is asserted purely; the razor delegates to it, so the razor logic is
  covered indirectly (source-contract) rather than by bUnit DOM interaction.

---

## Continuation — audit, behavior tests, clean baseline, publish correction (2026-10-08)

Same CHANGE_ID; this is a continuation of the same update, not a new entry.

### Corrections applied

1. **`RVideoV2Preview.razor` — cost-confirmation parity + warning removal.**
   The two re-render handlers passed `yesText: estimate.MissingPoints > 0 ? null : "..."` into
   `DialogService.ShowMessageBoxAsync`, which produced `CS8604` (possible null `yesText`) and did not
   mirror the V1 guard shape. Rewritten to mirror legacy `ConfirmAndRerenderSceneImageAsync` /
   video confirm exactly: **when points are missing, show an informational box with only a
   `cancelText:"Đóng"` and `return` (no Yes path); when sufficient, show the confirm box.** No billing
   rule, amount, or guard changed — only the dialog shape. `CS8604` for these lines is gone.

No other production change was required. The dirty policy, both actions, and the re-merge flow were
audited and found correct (see below); no root-cause bug was found beyond the dialog shape.

### Mode / input policy — verified rules

- The RVideo scene-video pipeline is **canonical `image_to_video` only**:
  `SceneVideoRenderHandler.JobTypeName = render_scene_video` → capability
  `RVideoVideoModelPolicy.CapabilityCode = rvideo_scene_video_generation` (provider `79ai`).
  `AiProviderCatalog` lists a generic `text_to_video` capability, but **no text-to-video mode is wired
  for RVideo scenes** (`TryBuildSceneVideoRenderInput` and `VideoRenderEligibilityService` both require a
  source image).
- `VideoRenderEligibilityService.GetVideoRenderEligibilityAsync` (VideoRenderEligibilityService.cs:113-129)
  emits `MissingImage` / `RVIDEO_VIDEO_IMAGE_REQUIRED` whenever there is **no usable source image,
  regardless of mode or prompt** (uses `RVideoEffectiveSceneImageSourceResolver` / shared reference).
- Therefore the dirty policy's "first generation requires `hasAnyImageSource`" is **correct and matches
  canonical behavior** — it does NOT wrongly force an image for a non-image mode, because no such mode
  exists in the canonical flow. Per the task rule ("Unsupported mode: keep existing validation, do not
  add capability; do not guess mode from model name"), no capability was added and no model-name
  guessing was introduced. **No dirty-policy change needed.**

### V1 parity — file:line comparison (image)

| Concern | V1 `RenderVideoJobs.razor` | V2 action `RVideoSceneImageRenderAction.cs` |
|---|---|---|
| Cost estimate source | `_initialEstimate.ImageRate` (7884) | `estimate.ImageRate` from `IRVideoInitialPointEstimateService` (116, 248) |
| Cost confirm before submit | `ConfirmAndRerenderSceneImageAsync` 7870-7932 | `HandleRerenderImageAsync` dialog (Preview 425-436) |
| Active-version dedup | `HasActiveImageVersionAsync` (7632) | identical (143) |
| Immutable version create | `CreateQueuedImageVersionAsync` (7700) | identical (179) |
| Billing intent | `PointBillingIntent.UserRerender` + `billingOperationId` (7730-7732) | identical (216-217) |
| Queue | `EnqueueAsync(SceneImageRenderWorkItemHandler.JobTypeName)` (7740) | identical (220-222, 242) |
| Logical request id | `BuildLogicalRequestId("render_job_scene_image_rerender", scene.Id, null)` (7694) | identical (176) |
| customerPointRate/Quality | `customerPointRate` (= ImageRate) + Premium/Standard (7724) | `estimate.ImageRate` + Premium/Standard (212-215) |

Note: V1 additionally calls `VideoRepo.UpdateSceneAsync(scene.Id, Draft, null, ...)` (7734) after the
version create. In V2 the equivalent persistence already happens **before** submit via
`SaveDraftOnlyAsync` (Preview 424 → `RVideoSceneDraftSaveAction` → `SaveSceneDraftAsync`, which writes
scene_prompt/image_prompt/video_prompt and status). The version create is the state driver; no second
scene write is needed. Persistence-ordering difference, not a behavior gap.

### V1 parity — file:line comparison (video)

| Concern | V1 `RenderVideoJobs.razor` | V2 action `RVideoSceneVideoRenderAction.cs` |
|---|---|---|
| Eligibility | UI-local `CanCreateSceneVideo` (4568) then `EnqueueSceneVideoAsync` (9880) | canonical `IVideoRenderEligibilityService.GetVideoRenderEligibilityAsync` (142) |
| Trusted payer | `RVideoPayerContexts.BuildRVideoTrustedPayerContextAsync` (9906) | `_payers.BuildRVideoTrustedPayerContextAsync` (155) |
| Shared reference | `TryApplySharedReferenceImage` (1994) | `ApplySharedReference` (209-222) — same `InvalidOperationException` guard |
| Duration/model override | `selectedOverride` + `RequestedModelCode/DurationSeconds` (9934-9956) | `ResolveRequestedModel` + `video_duration` (172-179) |
| Dedup | `EnqueueForSceneIfNoneActiveAsync(..., scene.Id, ...)` (9964) | identical (181, keyed on `scene.Id`) |
| Billing intent | `PointBillingIntent.UserRerender` (9840) | identical (166) |
| Event | `RVIDEO_VIDEO_RERENDER_ENQUEUED` (10014) | identical (199) |

V2's canonical eligibility guard is **stricter** than V1's UI-local `CanCreateSceneVideo`
(server-side `RVideoEffectiveSceneImageSourceResolver`, `IVideoPromptValidator`, version-status checks)
— proper reuse, not a bypass. No guard weakened.

### History / Selection / Merge — verification

- History lists the correct scene + media type: `ListImageVersionsAsync` / `ListSceneVideoVersionsAsync`
  (`scene.Id`, user); `IsVideo` set per list.
- Prompt snapshot belongs to the version (`ImagePromptSnapshot` / `VideoPromptSnapshot` shown in dialog).
- Pending/failed/unusable versions are **not selectable**: UI `CanSelect = status == "completed"` AND
  backend `SelectImageVersionAsync` / `SelectSceneVideoVersionAsync`
  (`SceneMediaVersioningService.cs:766, 1042`) enforce `WHERE ... AND status='completed' FOR UPDATE`.
- Selecting a version updates canonical active selection (`is_selected`, scene
  `selected_image_version_id`/`selected_video_version_id`, event `SCENE_IMAGE_VERSION_SELECTED`) and is
  read back by `GetSelected*VersionAsync` after reload.
- Unsaved draft protected: switching versions when the working prompt differs from the active baseline
  shows an explicit confirm ("Chọn phiên bản ảnh/video") — no silent draft loss.
- Merge uses **active** selected video versions in **scene order**, not latest-created:
  `IsCurrentFinalVideoAsync` (finalization 130-153) compares `ListFinalVideoVersionItemsAsync` against
  `GetSelectedVideoVersionAsync(OrderBy SceneIndex)` via `SequenceEqual`.
- Re-merge **not short-circuited by stale `FinalVideoUrl`**: `already_final` (finalization 69-74) only
  fires when `IsCurrentFinalVideoAsync` is true; selecting an older video makes it false → real merge.
- Duplicate merge blocked by canonical `EnqueueForLogCodeIfNoneActiveAsync` keyed on
  `rvideo-final-merge:{projectId}` → `AlreadyActive`.
- Merge not-ready returns `NotEnqueued("not_ready")`; failure never becomes fake success (finalization 83-95).

### Behavioral tests added (offline, no DB)

Extended `TodoX.Web.Tests/RVideoV2SceneRerenderHistoryTests.cs` (+11, 25 → 36): first-generation image
requirement by canonical image-to-video contract; prompt-edit/undo + image→video dependency combination;
unknown-source-image fall-through; canonical eligibility status semantics
(`MissingImage`/`AlreadyCompleted`/`AlreadyActive`/`InvalidPrompt` not eligible); re-merge guard contracts
(current-final-video comparison in scene order, duplicate-merge key, not-ready path). Behavioral asserts
on pure policy + canonical DTOs, plus traced source-contract asserts on the DB-backed finalization guard.

### Clean baseline (proven)

- Isolated worktree at source pre-task:
  `git worktree add --detach ..\todox-baseline-63b3e58 63b3e584c03d20e6b57717cc36615c577e24c959` (source HEAD).
  Contains **none** of the task's new untracked files.
- `TodoX.Web.Tests/TodoX.Web.Tests.csproj` is **gitignored** (`.gitignore:31`) and untracked → copied
  **into the baseline worktree only** (project file, not implementation) to run the same test project.
- Baseline: `dotnet test ... -c Release` in worktree → **22 failed / 1228 passed / 1250 total** (`baseline.trx`).
- Comparison by **failure identity**:
  - **New regressions: NONE** (set-difference empty).
  - All 21 current failures are a **strict subset** of the 22 baseline failures; one baseline failure
    (`RVideoAutosaveWorkflowTests.SceneGrid_IsTwoColumns...`) now passes (order/flaky, unrelated).
  - Current failures: Ai79TaskClient (2), BillingAndRatio (1), CorePlatformLifecycle (1), DanceSell (2),
    FavoriteServices (1), RDanceFashionDemo (10), RDanceStagedBilling (2), StaticImageBillingPolicy (1),
    TimelapsePhase2A (1). None reference RVideo V2.

### Exact checks run (this continuation)

- `dotnet build TodoX.Dashboard.sln -c Release` → **Build succeeded, 0 errors** (48 warnings, all pre-existing).
- `--filter FullyQualifiedName~RVideoV2SceneRerenderHistoryTests` → **36 passed / 0 failed**.
- `--filter "~RVideo|~SceneMedia|~VideoRender"` → **298 passed / 1 failed**; the failure is the
  pre-existing `StaticImageBillingPolicy...RVideoInitialEstimateWiresStaticImageBillingSetting`.
- Full `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release` → **1265 passed / 21 failed / 1286 total**;
  zero new regressions vs baseline.

### Publish result (corrected destination)

- `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release -o artifacts/publish` →
  **`artifacts/publish/TodoX.Web.dll` present** (fresh), 341 files under `artifacts/publish/wwwroot`,
  17 files at publish root. Canonical directory used; **`todox-dashboard` NOT appended**.
- Old folders still present and **NOT deleted**: `artifacts/publish/todox-dashboard`,
  `artifacts/publish/todox-landing`, plus a stray `artifacts/publish/wwwroot`.

### Runtime / UI — remaining limits

- **Still NOT runtime / visually verified.** No running app, browser, DB session, provider call, or paid
  job. Desktop/mobile button states, history dialog, version selection and re-merge snacks are asserted
  by behavioral + traced source-contract tests only.
- Live end-to-end (edit → save → re-render image → edit video → re-render video → history → select old
  video → reload → re-merge → Result uses that selection) outstanding; needs running instance + DB +
  provider credentials. Not claimed as complete.

### Frozen subsystems / DB — reconfirmed

- Provider payloads, pricing/charging rules, VOICE/AUDIO, polling, render workers, lifecycle, merge engine
  internals, V1/frozen UI: **UNTOUCHED**. No DB migration / schema change / SQL executed.
- Commit / Push / Deploy / Restart: **NOT EXECUTED**.