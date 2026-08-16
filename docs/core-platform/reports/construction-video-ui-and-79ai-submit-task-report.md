# Construction Video UI and 79AI Submit Task Report

## 1. Root Cause UI

Timelapse job detail already had rendering classes, but the active sweep and scanline contrast were still too subtle on the dark TodoX UI. Video card child elements also lacked enough width constraints at several levels, so real start/end reference media could influence card/grid sizing.

## 2. Root Cause Video Submit

79AI video submit can return a successful async id as top-level `id_base`. The TodoX video submit parser only checked legacy `task_id`, `taskId`, `request_id`, and `requestId`, so the success response looked like a missing task id and the informational success message was treated as a provider error.

## 3. Files Changed

- `TodoX.Web/Services/AiProviders/Ai79TaskClient.cs`
- `TodoX.Web/Components/Pages/TimelapseJobDetail.razor.css`
- `TodoX.Web.Tests/Ai79TaskClientTests.cs`
- `TodoX.Web.Tests/TimelapsePhase2CTests.cs`
- `docs/core-platform/reports/construction-video-ui-and-79ai-submit-task-report.md`

## 4. CSS/Layout Fix

Added `min-width: 0`, `max-width: 100%`, and `overflow: hidden` constraints to the stage grid, cards, previews, media, and clip thumbnail containers. Kept the main video preview at 16:9 and the start/end references in a compact thumbnail row.

## 5. Flash/Snip Animation Behavior

Strengthened the rendering-only dark overlay, diagonal image/video sweep, scanline thickness, and glow intensity. Waiting remains a softer shimmer, completed and failed cards do not receive active render animation, and the existing reduced-motion guard is preserved.

## 6. Parser Fix

Video submit now parses `id_base` from top-level response JSON, `videoInfo.id_base`, and `data.id_base` before falling back to legacy async id aliases. Image submit parsing remains on the existing `imageInfo.id_base` path.

## 7. Polling Verification

Polling contract was not changed. Image polling still sends `id_base`; video polling still sends `task_id` with the parsed async id value.

## 8. Error Display Behavior

Customer-facing Timelapse error text remains friendly and does not expose provider raw JSON or secrets. Real provider errors such as missing `resolution` still throw through `Ai79TaskSubmitException`.

## 9. Tests

Added regression coverage for:

- Video submit success with top-level `id_base`.
- Video submit success with `videoInfo.id_base`.
- Video submit success with `data.id_base`.
- Legacy video `task_id` and `request_id` aliases.
- Missing `resolution` provider error still throwing.
- UI source contracts for constrained video cards and compact input thumbnails.
- Rendering-only animation classes and reduced-motion preservation.

## 10. Build

Commands run:

- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --include TodoX.Web\Services\AiProviders\Ai79TaskClient.cs TodoX.Web.Tests\Ai79TaskClientTests.cs TodoX.Web.Tests\TimelapsePhase2CTests.cs`
  - Result: passed.
- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore`
  - Result: passed, 545 tests.
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`
  - Result: passed, 0 warnings, 0 errors.
- `git diff --check`
  - Result: passed; Git reported line-ending warnings only.

No production deploy, restart, migration, or SQL execution was performed.

## 11. Remaining Risks

No browser screenshot was captured in this task. UI verification is covered by source-contract tests; final visual confirmation should be done in the deployed dark TodoX UI with real 16:9 and 9:16 media.

## 12. Is Ready For Retry Test?

YES. Existing failed video clips should be retryable because the runtime already calls `SaveVideoSubmittedAsync` with the parsed `TaskId`; the parser now supplies the 79AI `id_base` value for successful video submit responses.

## 13. Final Commit SHA

Implementation commit before report: `c467e17`.

