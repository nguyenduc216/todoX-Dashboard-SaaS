# RVideo UI V2 — Architecture / Reuse Map (Phase 1 Audit)

> **Scope:** Read-only audit. No code/build/publish/commit. Legacy route frozen.
> **Monolith:** `TodoX.Web/Components/Pages/RenderVideoJobs.razor` (~6300 lines incl. @code) + `RenderVideoJobs.razor.css` (scoped)
> **Target:** Single RVideo Core (services/persistence/payloads/workers) + 2 UIs: Legacy + `Components/Pages/RVideo/` (V2)
> **Protected (frozen):** IMAGE/VIDEO render, provider payloads (79AI/KIE/YEScale/Gommo), VOICE/Vbee, POINT/BILLING, DB migrations, lifecycle workers.

## 1) Current Architecture Map

```
RenderVideoJobs.razor (@page "/render-video-jobs" — role: Creator/Admin)
  ├─ Tabs: Info (search/filter/table) | Preview (scene prompts) | Result (video preview)
  ├─ State (in-file): jobList, filteredJobs, selectedJob, draftSceneJson, speech flags, dialog params
  ├─ Handlers (in-file): OnSearch, OnSelectJob, OnSaveSceneJson, OnGenerateVideo, PromptSpeechHandler callbacks
  ├─ Dialogs: QuickPromptDialog, PromptAssistantGeneratedDialog, ServicePromptAssistantDialog, ScenePromptEditorDialog
  ├─ JS: wwwroot/js/todox-speech-input.js (Web Speech API wrapper)
  └─ Services (Core — reusable):
       RVideoJobService.cs -> RVideoEndpoints.cs / RVideoJobSettingsRepository.cs
       RVideoSceneJsonService.cs -> ScenePromptMetadata.cs / TodoXVideoPromptParser.cs
       RVideoInitialPointEstimateService.cs
       ServicePromptAssistantService.cs -> ServicePromptAssistantRepository.cs / PromptAssistantEndpoints.cs
       PromptSpeechHandler.cs / QuickPromptTranscriptMerger.cs
       VideoRenderRepository.cs (persistence)
```

Routing: `Program.cs` maps Blazor SSR + `MainLayout.razor` nav (`/render-video-jobs` active). `App.razor` is shell. No API versioning for V2 needed.

CSS: `RenderVideoJobs.razor.css` scoped + globals `wwwroot/css/app.css` + `todox-theme.css` (vars `--todox-*`, MudBlazor overrides, `.todox-*` utils). No `.rv2-*` namespace yet.

## 2.1) Trace Columns

`UI Feature | Current UI (file:line) | State | Handler | Service | Persistence | V2 Reuse | Group`

State/Handler inline in `RenderVideoJobs.razor @code` unless noted.

## 3) Info Tab — Reuse Map

| Feature | Legacy UI | State/Handler | Service/Persistence | V2 Strategy | Group |
|---|---|---|---|---|---|
| Job list + search/filter | `RenderVideoJobs.razor: L60-400` | `jobList/filteredJobs`, `OnSearch` | `RVideoJobService` → `RVideoEndpoints` / `VideoRenderRepository` | Reuse service, new V2 table component | B |
| Select job | same | `selectedJob`, `OnSelectJob` | same | same | B |
| Create job CTA | same | handler in @code | `RVideoJobService.CreateAsync` | Call directly from V2 | B |
| Scoped CSS | `RenderVideoJobs.razor.css` | - | - | New `RVideo/*.razor.css` with `.rv2-*` | A |
| Nav/routing | `MainLayout.razor` + `Program.cs` | - | - | Keep `/render-video-jobs` frozen; V2 at `/r-video` (proposal only) | A |

## 4) Preview Tab — Reuse Map

| Feature | Legacy UI | State/Handler | Service/Persistence | V2 Strategy | Group |
|---|---|---|---|---|---|
| Scene JSON draft/edit | `RenderVideoJobs.razor: L800-1200` | `draftSceneJson`, `OnSaveSceneJson` | `RVideoSceneJsonService` + `ScenePromptMetadata` / `TodoXVideoPromptParser` + `RVideoJobSettingsRepository` | Reuse parser/service; extract draft validator | B/C |
| Prompt assistant | dialogs `ServicePromptAssistantDialog` | dialog params | `ServicePromptAssistantService` / `Repository` / `PromptAssistantEndpoints` + `PromptAssistantCharacterReferenceSynchronizer` | Reuse service directly | B |
| Speech input | `QuickPromptDialog` + `todox-speech-input.js` | `PromptSpeechHandler` flags | `PromptSpeechHandler` / `QuickPromptTranscriptMerger` | Reuse handler + JS, new V2 button | B |
| Point estimate | inline | estimate state | `RVideoInitialPointEstimateService` | Direct reuse | B |
| Scene editor dialog | `ScenePromptEditorDialog.razor` | - | `RVideoSceneJsonService` | Share dialog or V2 copy with `.rv2-*` | A |

## 5) Result Tab — Reuse Map

| Feature | Legacy UI | State/Handler | Service/Persistence | V2 Strategy | Group |
|---|---|---|---|---|---|
| Render/Generate | `RenderVideoJobs.razor: L1400-1800` | `OnGenerateVideo` | `RVideoJobService` -> `RVideoEndpoints` (provider agnostic) | Direct reuse | B |
| Video preview/player | same | `selectedJob.resultUrl` | - | New V2 player markup (A) + same URL | A |
| Download/share | same | handler | - | New V2 actions | A |

## 6) Group Summary

- **A:** All markup/CSS/MudBlazor layout, nav, player chrome, dialog chrome.
- **B:** All `Services/*` + `wwwroot/js/todox-speech-input.js` + dialogs as-is.
- **C:** Draft scene JSON validation/normalization + `filteredJobs` selector + any @code helper that parses `TodoXVideoPromptParser` output before calling `RVideoSceneJsonService`. Location: `RenderVideoJobs.razor @code ~L500-1800`. Risk: regression if extracted with altered semantics. Test: unit test parser + service; Blazor bUnit for V2 component binding.
- **D:** None confirmed blocking; if V2 spec adds new provider/model fields -> owner decision.

## 7) CSS Isolation

- Scoped: `Components/Pages/RVideo/*.razor.css` only. No edits to `RenderVideoJobs.razor.css`.
- Global stays: `app.css`, `todox-theme.css` (vars `--todox-*`).
- Namespace: `.rv2-*` for all V2 classes; never reuse `.todox-*` selectors for new layout.
- MudBlazor: reuse theme; V2 overrides inside `.rv2-*` scope only.
- Breakpoints: reuse global; add V2 grid under `.rv2-grid`.

## 8) Routing (Proposal Only — No Route Change Now)

- Legacy frozen: `/render-video-jobs` (unchanged in `MainLayout.razor`).
- V2: `/r-video` + `/r-video/:jobId` (new folder `Components/Pages/RVideo/`).
- Switch: link/button `"Try new UI"` on Legacy -> V2 (same JobId query), and `"Back to legacy"` in V2. No redirect.
- Auth: same `[Authorize(Roles="Creator,Admin")]`.

## 9) Proposed V2 Component Tree

```
Components/Pages/RVideo/
  RVideoIndex.razor (+ .razor.css) — tabs container, route /r-video
  RVideoInfoTab.razor — table/search (A) + injects RVideoJobService (B)
  RVideoPreviewTab.razor — scene editor (C extraction) + speech (B)
  RVideoResultTab.razor — player + actions (A/B)
  Shared: RVideoJobState.cs (C) — selectedJob/filtered logic extracted
Services/VideoRender/RVideoSceneDraftValidator.cs (C) — new, pure logic from @code
```

No changes to `RenderVideoJobs.razor` except optional `"Try new UI"` link (Phase 2).

## 10) Minimal Extraction List (C)

1. `RVideoJobState` / `RVideoJobFilter` — from `RenderVideoJobs.razor @code` filteredJobs + selection.
2. `RVideoSceneDraftValidator` — normalize/validate `draftSceneJson` before `RVideoSceneJsonService.SaveAsync`.

## 11) Risk Matrix

| Risk | Area | Mitigation |
|---|---|---|
| Semantic drift in extracted validator | C | Copy exact @code logic, add regression test vs Legacy |
| CSS leak to Legacy | A | `.rv2-*` only + scoped CSS |
| Route collision | Routing | V2 under `/r-video`, legacy untouched |
| Protected payload change | Frozen | No provider/point/billing edits |

## 12) Phase 2 Proposal (Not Started)

- **Create:** `Components/Pages/RVideo/*`, `Services/VideoRender/RVideoSceneDraftValidator.cs`, `Services/VideoRender/RVideoJobState.cs` (or `State/RVideo/`)
- **Modify (minimal):** `MainLayout.razor` (add V2 nav item hidden behind flag) + Legacy optional switch link. No logic change.
- **Tests:** Unit: `RVideoSceneDraftValidatorTests`, `TodoXVideoPromptParserTests` reuse; bUnit: `RVideoPreviewTabTests` binding; E2E smoke: Info->Preview->Result happy path.
- **Validation before done:** `dotnet build`, `dotnet test`, `dotnet publish -o artifacts/publish/todox-dashboard`.
- **Frozen untouched:** `Services/VideoRender/VideoRenderRepository.cs`, provider payloads, `RVideoInitialPointEstimateService` internals, `Admin*`, voice/billing.

> End of Phase 1. No implementation/build/publish/commit performed.




## 2) Group Classification Rule

- **A Presentation Only:** Markup/CSS/dialog layout, no business logic split.
- **B Direct Reuse:** Call Core service as-is from V2 (no extraction).
- **C Shared Extraction Required:** Logic embedded in razor @code must be extracted to Core/shared service/component before V2 reuse (list location + risk + test).
- **D Functional Gap — REQUIRES OWNER DECISION:** Spec asks for behavior not in Legacy or ambiguous.

