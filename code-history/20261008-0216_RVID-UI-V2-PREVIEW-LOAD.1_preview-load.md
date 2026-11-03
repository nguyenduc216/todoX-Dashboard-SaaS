# RVID-UI-V2-PREVIEW-LOAD.1 — V2 Preview load orchestration + on-demand media

CHANGE_ID: RVID-UI-V2-PREVIEW-LOAD.1
Date: 2026-10-08
Agent: Claude Code
Branch: fix/prompt-assistant-tts-validation

## Requested scope

Optimize the time-to-usable for the RVideo V2 **XEM TRƯỚC (Preview)** tab:

1. Show the selected scene **early**; avoid a long/meaningless empty area
   (real image: scene selectors 1–5 render but the content area below stays
   blank, so users think the page is broken).
2. Load heavy media for **other** scenes **on demand**, not all at once.

Precondition preserved: the completed Information implementation
(RVID-UI-V2-PHASE3A.1) is kept as-is.

## Allowed files / behaviors

- TodoX.Web/Components/Pages/RVideo/RVideoV2Preview.razor (+ .razor.css)
- TodoX.Web.Tests/RVideoV2PreviewLoadTests.cs (new focused coverage)
- this history file.

## Out of scope / frozen areas — explicitly NOT touched

- render / provider / polling / lifecycle: unchanged.
- VOICE / AUDIO: unchanged.
- POINT / BILLING (StaticImageBillingPolicy, RDance staged billing): unchanged.
- IMAGE GENERATION, VIDEO GENERATION (RDance/DanceSell), Timelapse, Finalizer, Merge: unchanged.
- Backend provider/API contracts: unchanged. No new service was created.
- No render request or provider call is made merely by opening Preview.
- LEGACY UI (RenderVideoJobs.razor) preserved.
- No DB migration / schema change / SQL. No commit/push/deploy/restart.

## Bottleneck evidence (root cause)

Traced the Preview open path only (no full-repo scan, no history scan):

1. `RVideoV2Shell.razor:82 ReloadAsync()` calls
   `RVideoJobs.GetByJobIdAsync(...)` — ONE canonical request returning the whole
   `RVideoJobView` (project + all scene metadata + URLs). This request is kept
   (not split into N+1).
2. `RVideoV2Preview.razor` (before): `protected override async Task OnInitializedAsync()`
   did `_opts = await ModelOptions.GetOptionsAsync();` — an async provider-catalog
   call that **blocks the first render** of the whole tab. `OnParametersSetAsync`
   then issued a **second** `GetOptionsAsync()` when `_opts.Count==0` (duplicate fetch).
   => the scene content area could not paint until the model-options call resolved.
3. `RVideoV2Preview.razor` (before): the desktop table eagerly mounted a
   `<video src=... preload="metadata">` inside `@foreach` over **all** scenes, so
   opening Preview triggered concurrent media metadata fetches for every scene.

Bottleneck = selected-scene content was gated behind (a) a blocking
model-options await and (b) eager media mounting of all scenes.

## Changes

### Selected / first scene prioritized

- Replaced the blocking `OnInitializedAsync` + duplicate `OnParametersSetAsync`
  model-option fetch with a **synchronous** `OnParametersSet()`:
  - selects the first scene (`OrderBy(SceneIndex).First().Id`) immediately from
    already-loaded metadata — no await;
  - adopts `ModelOptions.GetCachedOptions()` synchronously when available (reuses
    the existing cached list; no new service).
- Model options load in the background in `OnAfterRenderAsync(firstRender)` and
  repaint via `StateHasChanged()`. First paint no longer waits for them.
- Metadata readiness is now decoupled from media readiness.

### Heavy media loaded on demand

- Desktop table only mounts the real `<video preload="metadata">` when
  `SelectedSceneId == s.Id`; every other scene shows a lightweight
  `.rv2-vid-lazy` placeholder (no media request).
- `<img>` uses `loading="lazy"`. Placeholder keeps the real thumb footprint
  (100x56) so layout/aspect stays stable.
- Mobile already rendered one active scene; its nav now goes through the same
  `SelectScene` path.
- No prefetch added (no evidence it was needed; concurrency kept bounded).

### Loading / empty / error / ready states

- `JobView == null`  -> `.rv2-skeleton-grid` shimmer skeleton (no blank area).
- `JobView != null && !_optsReady` -> metadata (scene count, status chips) with
  per-cell skeletons; never blank, never blocked.
- ready -> real rows; empty video -> "Chưa có"; empty media -> "Chưa có media".
- media load failure -> per-scene `.rv2-media-error` with a **Thử lại** retry.
- No infinite spinner was introduced; no provider timeout touched.

### Stale-load / dedupe guards

- `_sceneVersion++` on every real scene switch (version guard).
- `SelectScene` no-ops on re-selecting the current scene (dedupe).
- Media errors tracked per scene id in `Dictionary<long,bool> _mediaErrors` so one
  failing scene cannot blank the whole Preview; a new selection clears that
  scene's error.
- `RetryMediaLoad(sceneId)` only clears the local error flag so the existing
  `<video>` src re-mounts — it does **not** create a render job or provider call.
- `IDisposable` implemented; `_gate` SemaphoreSlim disposed.

## Reuse map (UI action -> existing service/state)

- Scene metadata -> `IRVideoJobService.GetByJobIdAsync` (existing, unchanged).
- Model options -> `IRVideoSceneVideoModelOptionsService.GetOptionsAsync/GetCachedOptions`.
- Draft validate/save -> `IRVideoSceneDraftValidator` / `IRVideoSceneDraftSaveAction`.
- Add/move/delete -> `IRVideoSceneCollectionAction`; add defaults -> `IRVideoSceneAddDefaultResolver`.
- Video dialog -> existing `RVideoV2MediaPreviewDialog`; ref lightbox -> existing state.

## Files changed

- `TodoX.Web/Components/Pages/RVideo/RVideoV2Preview.razor`
- `TodoX.Web/Components/Pages/RVideo/RVideoV2Preview.razor.css`
- `TodoX.Web.Tests/RVideoV2PreviewLoadTests.cs` (new)
- this history file.

## Verification

- `dotnet build TodoX.Dashboard.sln -c Release` -> **Build succeeded, 0 Error(s)** (48 warnings, pre-existing).
- Focused `FullyQualifiedName~RVideoV2PreviewLoadTests` -> **25 passed / 0 failed**.
- `FullyQualifiedName~RVideoV2ShellTests` -> **19 passed / 0 failed**.
- `FullyQualifiedName~RVideoP3B` -> **32 passed / 0 failed** (RVideoP3B1 + RVideoP3B2).
- Broad `FullyQualifiedName~RVideo` -> **258 passed / 1 failed**; the single failure is the
  documented pre-existing `StaticImageBillingPolicyRegressionTests.RVideoInitialEstimateWiresStaticImageBillingSetting`
  (frozen Billing area; reads `RVideoInitialPointEstimateService.cs`, which `git status`
  reports UNMODIFIED — the method merely contains "RVideo").
- Baseline proof: with the two Preview files stashed, the same 21 full-suite failures
  reproduce (see below), so none are caused by this change.
- Full suite `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release`
  -> **1226 passed / 21 failed**. All 21 are pre-existing failures in frozen/protected
  areas: BillingAndRatio, RDance/DanceSell, Ai79TaskClient, Timelapse, StaticImageBillingPolicy,
  CorePlatformLifecycle, FavoriteServices. Verified identical with changes stashed (baseline).
- Publish `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release -o artifacts/publish`
  -> `artifacts/publish/TodoX.Web.dll` refreshed (2:15 AM) and published
  `wwwroot/TodoX.Web.styles.css` contains the new `.rv2-media-placeholder[b-8hy39uc2p1]`,
  `.rv2-skeleton-row[b-8hy39uc2p1]`, `.rv2-vid-lazy[b-8hy39uc2p1]`.
  `artifacts/publish/todox-dashboard/` was **NOT** touched (mtime unchanged 1:35 AM) —
  canonical publish path respected per task constraint.

## Performance evidence

- **NOT RUNTIME VERIFIED** — no running app/browser session was available in this task.
  No milliseconds were invented and no speedup is claimed. Static/structural evidence
  only: first render no longer awaits the model-options provider call, and non-selected
  scenes emit no `<video src>` (so they trigger no media request).

## UI verification

- **NOT VISUALLY VERIFIED** (desktop/mobile/slow-network). Structural + published-CSS
  checks only; Vietnamese glyph rendering and the empty->skeleton->ready transition are
  not observed at runtime.

## Frozen subsystems / DB

- Billing, RDance, DanceSell, 79Ai, Voice, Timelapse, Finalizer, Merge, provider/render/
  polling/lifecycle: UNTOUCHED. Legacy UI preserved. No DB migration/schema/SQL executed.
  No render request or provider call added on Preview open.

## Git / deploy

- Commit SHA: N/A. Commit/Push/Deploy/Restart: **NOT EXECUTED** (per task DO NOT).

## Limitations / remaining blockers

- Live browser + runtime performance verification outstanding (needs a running instance).
- `@onerror` on `<video>` handles network/media failures; a client-side decode failure
  that never fires `error` (rare) would not flip the per-scene error state.