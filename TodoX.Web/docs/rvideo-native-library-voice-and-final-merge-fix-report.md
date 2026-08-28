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
- Audio completion persists the local media first. Audio completion no longer marks the raw selected video as muxed.
- Mux completion is the only path that writes `voice_audio_version_id` onto the selected scene video.
- Mux input ownership is checked against the target scene and project.
- Existing voice text/instructions are preserved during draft saves when the incoming prompt does not contain replacements.
- Scene create, load, add, save, update, and replace SQL surfaces include the voice metadata columns.
- AUTO lifecycle evaluates selected scene versions using voice-aware final-ready rules and enqueues one merge job with `rvideo-auto-merge:{projectId}`.
- Existing completed scene video versions remain eligible for continuation; the lifecycle does not regenerate them.

## N8N Behaviors Adopted

The SaaS implementation preserves the useful reliability properties of the reference workflow: per-scene identity, one TTS request per scene, local cached MP3 authority, and continuation through the same scene finalization path. It does not copy legacy `public.todox_*` tables or aggregate narration into one MP3.

## Project 11 Recovery

For a legacy `LIBRARY` project whose successful scene videos are already selected, the lifecycle can derive missing scene voice text and instructions from canonical scene prompt metadata, enqueue only the missing per-scene audio work, mux each matching scene, and then enqueue the final merge. No video regeneration is introduced by this change.

## Changed Files

- `TodoX.Web/Models/RVideoModels.cs`
- `TodoX.Web/Models/VideoRenderModels.cs`
- `TodoX.Web/Services/VideoRender/VideoRenderRepository.cs`
- `TodoX.Web/Services/VideoRender/SceneVideoRenderHandler.cs`
- `TodoX.Web/Services/VideoRender/RVideoSceneAudioAutoChainService.cs`
- `TodoX.Web/Services/VideoRender/RVideoSceneMediaFinalizerService.cs`
- `TodoX.Web/Services/VideoRender/SceneAudioMuxHandler.cs`
- `TodoX.Web/Services/VideoRender/SceneMediaVersioningService.cs`
- `TodoX.Web/Services/VideoRender/RVideoLifecycleWorker.cs`
- `TodoX.Web.Tests/VbeeSceneRuntimeTests.cs`
- `TodoX.Web.Tests/RVideoVoiceRuntimeTests.cs`

No database migration was created or executed.

## Validation

- `dotnet restore TodoX.Dashboard.sln`: passed.
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore --disable-build-servers`: passed, 0 errors. Existing generated Razor nullable warnings were reported in the initial build.
- Focused tests:
  `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --disable-build-servers --filter "FullyQualifiedName~RVideoVoiceRuntimeTests|FullyQualifiedName~VbeeSceneRuntimeTests|FullyQualifiedName~TodoXVideoPromptParserTests|FullyQualifiedName~RVideoRuntimeSqlTests"`: 16 passed.
- Full `TodoX.Web.Tests`: 777 passed, 7 unrelated pre-existing failures.
- Full `TodoX.Web.Phase1B.Tests`: 231 passed, 8 unrelated pre-existing failures.
- `git diff --check`: passed.
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --verbosity minimal`: not clean because the repository already contains many unrelated whitespace diagnostics across existing files; no bulk formatting was applied.
- Publish:
  `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore --disable-build-servers -o artifacts\publish\todox-dashboard`: passed.

## Git

Commit SHA: `e922ac6f1b5fc5d361b93b515e087bb2fe950243`
Pushed branch: `integration/rdance-on-construction-video-core`
