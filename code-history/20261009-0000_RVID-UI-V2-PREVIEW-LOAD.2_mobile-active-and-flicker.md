# RVID-UI-V2-PREVIEW-LOAD.2 — mobile active-scene indicator + options prewarm

CHANGE_ID: RVID-UI-V2-PREVIEW-LOAD.2
Date: 2026-10-09
Agent: Cline
Branch: fix/prompt-assistant-tts-validation

## Requested scope

Follow-up to RVID-UI-V2-PREVIEW-LOAD.1, for the RVideo **V2** XEM TRƯỚC (Preview) tab only:

1. Mobile: the active scene button (round) must clearly show **which scene is active**
   (the "viền vàng" / yellow ring).
2. Reduce remaining stutter/flicker when the scene UI loads (data arriving late).

## Allowed files / behaviors

- TodoX.Web/Components/Pages/RVideo/RVideoV2Preview.razor (+ .razor.css)   [V2 only]
- TodoX.Web.Tests/RVideoV2PreviewLoadTests.cs (extend)
- this history file.

## Out of scope / frozen areas — explicitly NOT touched

- RVideo **V1** (legacy): `RenderVideoJobs.razor` — UNCHANGED.
- Shared services (`RVideoSceneVideoModelOptionsService`, `RVideoSceneDraftValidator`),
  `RVideoV2Shell.razor`, `RVideoV2Info.razor`, `RVideoV2Result.razor`, `Program.cs` — UNCHANGED.
- IMAGE GENERATION, VIDEO GENERATION, VOICE/AUDIO, POINT/BILLING, provider payload
  contracts, migrations/DB: UNCHANGED.
- No render request or provider call added on Preview open.
- No commit/push/deploy/restart (per task).

## Root cause

### Issue 1 — mobile active-scene indicator weak
- Markup already binds `rv2-nav-active` when `SelectedSceneId == s.Id` (V2Preview.razor
  lines 47/119). The active **CSS** rule was `.rv2-nav-active{outline:2px solid #ffca28}`
  (specificity 0,1,0) competing with `.rv2-nav-dot.rv2-status-*` (0,2,0) — it only "won"
  via `!important`, and the visual cue (2px outline) was easy to miss on a small phone,
  especially on a green "ready" dot. Indicator was also color-only.

### Issue 2 — skeleton→content flicker on entry
- Options were fetched only in `OnAfterRenderAsync(firstRender)`, i.e. **after** first
  paint. On the first entry per circuit the user saw the skeleton shell then a swap to
  content (flicker/stutter). The fetch could also be started again later.

## Changes

### Issue 1 — unambiguous mobile active ring (V2Preview.razor.css)
- Replaced `.rv2-nav-active{outline:...}` with a higher-specificity rule
  `.rv2-nav-dot.rv2-nav-active` (0,2,0) that needs no `!important`:
  - bright yellow ring (`border-color:#ffca28;border-width:3px`),
  - halo + glow via `box-shadow` (double ring so it reads on any status color),
  - `transform:scale(1.22)`,
  - a persistent `::after` circular **check badge** (`✓`) so active scene is identifiable
    by shape, not color alone.
- Added `position:relative` to the base `.rv2-nav-dot` rule so the badge anchors correctly.

### Issue 2 — prewarm options off the render path (V2Preview.razor)
- New field `Task<IReadOnlyList<SceneVideoModelOption>>? _optsTask`.
- `OnParametersSet()` now, when options are not cached, **starts the single background
  fetch** (`_optsTask = ModelOptions.GetOptionsAsync()`) instead of waiting.
- `OnAfterRenderAsync(firstRender)` reuses that same task (`_optsTask ??= ...` then
  `await _optsTask`) — no duplicate fetch, deterministic one-time swap.

## Files changed

- `TodoX.Web/Components/Pages/RVideo/RVideoV2Preview.razor`
- `TodoX.Web/Components/Pages/RVideo/RVideoV2Preview.razor.css`
- `TodoX.Web.Tests/RVideoV2PreviewLoadTests.cs`
- this history file.

## Verification

- `dotnet build TodoX.Web/TodoX.Web.csproj -c Release` -> **Build succeeded, 0 Error(s)**
  (46 warnings, pre-existing: auto-generated CS8669 + a V1 `RenderVideoJobs.razor` MUD0002).
- `FullyQualifiedName~RVideoV2PreviewLoadTests` -> **28 passed / 0 failed** (25 prior + 3 new).
- `FullyQualifiedName~RVideoV2|~RVideoP3B` -> **124 passed / 0 failed** (no regression).

## UI / runtime verification

- **NOT RUNTIME VERIFIED** and **NOT VISUALLY VERIFIED** — no browser/app session available.
  No timings invented. Evidence is structural (CSS specificity + source contract tests) only.

## Git / deploy

- Commit/Push/Deploy/Restart: **NOT EXECUTED** (per task).

## Limitations / remaining blockers

- Live mobile rendering of the active ring and the flicker fix is unverified until a running
  instance/browser is available.