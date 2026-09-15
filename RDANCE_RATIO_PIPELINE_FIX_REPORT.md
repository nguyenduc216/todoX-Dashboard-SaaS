# RDance Ratio Pipeline Fix Report

## Git
Branch: integration/rdance-on-construction-video-core
Base SHA: dbf320e3ed14ef86d904fcba891df6f2b2a87c4b
Final SHA: Recorded after commit in the terminal result.
Push: Recorded after push in the terminal result.

## Root cause
Reference hard-coded ratio: 79AI reference generation used an effective 16:9 ratio instead of the selected job ratio.
Motion provider invalid ratio: 79AI Kling Motion 3 only accepts ratio=default, but the selected project ratio could be passed through.
Runtime provider error: 79AI rejected ratio=9:16 for kling_video_motion_3 because only default is allowed.

## Target behavior
Selected project ratio: Persisted canonical job.Ratio, limited to 9:16 or 16:9.
Reference image ratio: Uses the normalized selected job.Ratio.
Kling Motion ratio: Uses provider-specific ratio=default for kling_video_motion_3.

## Reference generation
Service method: DanceSellReferenceImageService.GenerateAsync
Provider method: Ai79DanceSellReferenceProvider.SubmitAsync
Actual outbound field: Ai79TaskSubmitRequest.Options["ratio"]
9:16 test: PASS - captured 79AI request Options["ratio"] and request JSON both equal 9:16.
16:9 test: PASS - captured 79AI request Options["ratio"] and request JSON both equal 16:9.
Hard-coded 16:9 removed: YES

## Reference regeneration
Ratio change handling: UpdateBusinessAsync detects canonical ratio changes and resets prepared reference state.
Old reference invalidation: Prepared reference URL/status/approval and selected versions are cleared through ResetReferenceAsync.
History compatibility handling: Reference reuse and approval/selection require request_json.ratio to match current job.Ratio.

## Motion generation
Model: kling_video_motion_3
Image input: Prepared reference image uploaded to 79AI and used as ImageUrl.
Motion input: MotionVideoUrl uploaded to 79AI and used as VideoUrl.
Provider ratio: default
9:16 project test: PASS - model-specific provider ratio resolves to default.
16:9 project test: PASS - model-specific provider ratio resolves to default.

## Error handling
Old error: Provider submit errors could surface as source-preparation failures.
New error: Submit-stage failures say the video request could not be sent to 79AI and include sanitized provider detail.
Provider error sanitized: YES

## Database
Schema migration: NO
Data migration: NO

## Provider safety
79AI used: YES
YEScale touched: NO
YEScale MCP called: NO
Other provider routes changed: NO

## Validation
Build: PASS - dotnet build TodoX.Dashboard.sln -c Release --no-restore (0 warnings, 0 errors).
Tests: PASS - 16/16 RDance tests passed.
git diff --check: PASS
dotnet format: Scoped RDance files PASS. Solution-wide verification reports pre-existing whitespace violations in unrelated files; no mass formatting was applied.
Publish: PASS - artifacts\publish\todox-dashboard

## Acceptance checklist
- [x] 9:16 project generates 9:16 reference
- [x] 16:9 project generates 16:9 reference
- [x] Reference provider has no overriding hard-coded 16:9
- [x] Kling Motion 3 receives ratio=default
- [x] Kling Motion does not receive 9:16/16:9 directly
- [x] PreparedReferenceUrl is used as motion image source
- [x] Ratio change requires/refires appropriate reference regeneration
- [x] Existing project ratio remains persisted
- [x] No image stretching
- [x] No unnecessary DB migration
- [x] YEScale untouched
- [x] Build passed
- [x] Tests passed
- [x] Publish passed
- [ ] Code pushed
