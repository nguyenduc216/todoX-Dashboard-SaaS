# RVIDEO Native/Library Voice and Final Merge Fix

Date: 2026-08-28
Branch: `integration/rdance-on-construction-video-core`

## Summary

The broken flow discarded scene voice metadata, passed null voice fields into scene-video jobs, marked raw videos as audio-linked before muxing, and did not automatically enqueue the project merge after all scenes became final-ready.

## Implemented Behavior

- Canonical voice modes remain `NONE`, `NATIVE`, and `LIBRARY`.
- `NONE` and `NATIVE` require only a completed selected scene video.
- `NATIVE` composes the scene visual prompt with native speech, delivery, and lip-sync instructions. The scene voice text and instruction are also carried in the child work item.
- `NATIVE` does not enqueue Vbee audio or FFmpeg mux work.
- `LIBRARY` resolves dialogue from the scene's own voice metadata and creates one logical audio request per scene.
- AUTO lifecycle now hydrates legacy scene voice metadata before voice gating, audio chaining, and merge readiness checks.
- Hydration persists `voice_enabled`, `speaker_key`, `voice_text`, and `voice_instruction` only when recoverable data is missing, and it keeps user-edited values intact.
- Audio completion persists the local media first. Audio completion no longer marks the raw selected video as muxed.
- Scene mux completion now registers the muxed media separately and writes its media id onto the completed scene-video version.
- Mux input ownership is checked against the target scene and project.
- Existing voice text/instructions are preserved during draft saves when the incoming prompt does not contain replacements.
- Scene create, load, add, save, update, and replace SQL surfaces include the voice metadata columns.
- AUTO lifecycle evaluates selected scene versions using voice-aware final-ready rules and enqueues one merge job with `rvideo-auto-merge:{projectId}`.
- Existing completed scene video versions remain eligible for continuation; the lifecycle does not regenerate them.
- Final merge now retries with a normalized transcode path when fast concat copy fails.

## N8N Behaviors Adopted

The SaaS implementation preserves the useful reliability properties of the reference workflow: per-scene identity, one TTS request per scene, local cached MP3 authority, and continuation through the same scene finalization path. It does not copy legacy `public.todox_*` tables or aggregate narration into one MP3.

## Project 11 Recovery

For a legacy `LIBRARY` project whose successful scene videos are already selected, the lifecycle can derive missing scene voice text and instructions from canonical scene prompt metadata, enqueue only the missing per-scene audio work, mux each matching scene, and then enqueue the final merge. No video regeneration is introduced by this change.

## Changed Files

- `TodoX.Web/Components/Pages/RenderVideoJobs.razor`
- `TodoX.Web/Services/VideoRender/RVideoLifecycleWorker.cs`
- `TodoX.Web/Services/VideoRender/SceneAudioMuxHandler.cs`
- `TodoX.Web/Services/VideoRender/VideoRenderMergeHandler.cs`
- `TodoX.Web/Services/VideoRender/VideoRenderRepository.cs`
- `TodoX.Web.Tests/RVideoVoiceRuntimeTests.cs`
- `TodoX.Web.Tests/RenderVideoJobsLayoutTests.cs`
- `TodoX.Web.Tests/VbeeSceneRuntimeTests.cs`
- `TodoX.Web/Tests/RVideoRuntimeSqlTests.cs`

No database migration was created or executed.

## Validation

- `dotnet restore TodoX.Dashboard.sln`: passed.
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore --disable-build-servers`: passed after shutting down the build server, 0 errors.
- Focused tests:
  `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --disable-build-servers --filter "FullyQualifiedName~RVideoVoiceRuntimeTests|FullyQualifiedName~VbeeSceneRuntimeTests|FullyQualifiedName~RenderVideoJobsLayoutTests"`: 28 passed.
  `dotnet test TodoX.Web\Tests\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --disable-build-servers --filter "FullyQualifiedName~RVideoRuntimeSqlTests|FullyQualifiedName~RVideoProviderPollingRegressionTests"`: 100 passed, 1 unrelated pre-existing failure.
- Full `TodoX.Web.Tests`: 784 passed, 7 unrelated pre-existing failures.
- Full `TodoX.Web.Phase1B.Tests`: 233 passed, 8 unrelated pre-existing failures.
- `git diff --check`: passed.
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --verbosity minimal --include ...`: passed for the touched files.
- Publish:
  `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore --disable-build-servers -o artifacts\publish\todox-dashboard`: passed.

## Git

Implementation commit SHA: `4108e8aefc54df53412d9379445dd3d88fd640d1`
Pushed branch: `integration/rdance-on-construction-video-core`

## ReplaceScenes SQL contract fix

`VideoRenderRepository.ReplaceScenesAsync()` was still inserting `voice_enabled`, `speaker_key`, `voice_text`, and `voice_instruction` values without listing those columns in the target column list, which would fail on PostgreSQL with `INSERT has more expressions than target columns`.

The earlier tests only checked for voice-related substrings and did not verify the target-column contract against the VALUES expression list, so the mismatch survived review.

The INSERT column list now includes the four voice columns in order, aligned with the existing parameter derivation. The regression test now extracts the full INSERT block, counts target columns against VALUES expressions, and asserts the exact column-to-parameter mapping.

Muxed scene-video storage was left with the current repository contract: `storage_key` remains provider-provenance data from version creation, while mux completion continues to update the canonical selected output fields (`result_media_id`, `public_url`, and `source_file_path`). I verified the current completion contract and did not widen it to mutate `storage_key` without a schema-backed invariant change.

Changed files and methods:

- `TodoX.Web/Services/VideoRender/VideoRenderRepository.cs`
- `TodoX.Web.Tests/RVideoRuntimeSqlTests.cs`

Focused validation, full validation, build, publish, and git results will be filled in after the final command run.
