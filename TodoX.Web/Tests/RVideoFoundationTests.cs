using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoFoundationTests
{
    [Fact]
    public void ImportAcceptsNarrationAliasesAndNormalizesIndexes()
    {
        var service = new RVideoSceneJsonService();
        var scenes = service.Import("""
        {
          "video_title": "Demo",
          "scenes": [
            { "scene": 7, "duration_seconds": 4, "image_prompt": "one", "voice_over": "hello" },
            { "scene": 2, "duration_seconds": 10, "image_prompt": "two", "script": "world" }
          ]
        }
        """);

        Assert.Equal(new[] { 1, 2 }, scenes.Select(x => x.SceneIndex));
        Assert.Equal("hello", scenes[0].DialogueText);
        Assert.Equal("world", scenes[1].DialogueText);
        Assert.Equal(10, scenes[1].DurationSeconds);
    }

    [Fact]
    public void ExportOrdersScenesAndPreservesRuntimeFields()
    {
        var service = new RVideoSceneJsonService();
        var json = service.Export("Demo", new[]
        {
            new RVideoSceneEditorItem(2, "End", 6, "b", "move b", "bye", null, "bad", 1.1m),
            new RVideoSceneEditorItem(1, "Hook", 4, "a", "move a", "hi", null, null, null)
        });

        Assert.True(json.IndexOf("\"scene\": 1", StringComparison.Ordinal) < json.IndexOf("\"scene\": 2", StringComparison.Ordinal));
        Assert.Contains("\"dialogue_text\": \"hi\"", json);
        Assert.Contains("\"negative_prompt\": \"bad\"", json);
    }

    [Fact]
    public void AutoLifecycleMovesFromImagesToVideoAndThenFinalizer()
    {
        var imageReady = RVideoRules.Evaluate(RVideoExecutionModes.Auto,
            new[] { VideoSceneStatuses.ImageReady, VideoSceneStatuses.ImageReady }, false);
        var videoReady = RVideoRules.Evaluate(RVideoExecutionModes.Auto,
            new[] { VideoSceneStatuses.VideoReady, VideoSceneStatuses.VideoReady }, false);

        Assert.True(imageReady.ShouldQueueVideo);
        Assert.False(imageReady.ShouldFinalize);
        Assert.True(videoReady.ShouldFinalize);
        Assert.Equal(RVideoStages.Result, videoReady.Stage);
    }

    [Theory]
    [InlineData(new[] { VideoSceneStatuses.Draft, VideoSceneStatuses.Draft }, true)]
    [InlineData(new[] { VideoSceneStatuses.ImageReady, VideoSceneStatuses.Draft }, true)]
    [InlineData(new[] { VideoSceneStatuses.ImageReady, VideoSceneStatuses.Failed }, false)]
    [InlineData(new[] { VideoSceneStatuses.ImageReady, VideoSceneStatuses.ImageReady }, false)]
    [InlineData(new[] { VideoSceneStatuses.VideoRendering, VideoSceneStatuses.ImageReady }, false)]
    [InlineData(new[] { VideoSceneStatuses.VideoReady, VideoSceneStatuses.VideoReady }, false)]
    public void AutoImageResumeOnlyTargetsMissingOrFailedStates(string[] statuses, bool expected)
        => Assert.Equal(expected, RVideoRules.NeedsImageWork(statuses));

    [Fact]
    public void PartialVideoFailureStillFinalizesSuccessfulClips()
    {
        var decision = RVideoRules.Evaluate(RVideoExecutionModes.Auto,
            new[] { VideoSceneStatuses.VideoReady, VideoSceneStatuses.Failed }, false);

        Assert.True(decision.ShouldFinalize);
        Assert.False(decision.TerminalFailure);
    }

    [Fact]
    public void AllVideoFailuresAreTerminalWithoutFinalizer()
    {
        var decision = RVideoRules.Evaluate(RVideoExecutionModes.Auto,
            new[] { VideoSceneStatuses.Failed, VideoSceneStatuses.Failed }, false);

        Assert.False(decision.ShouldFinalize);
        Assert.True(decision.TerminalFailure);
    }

    [Fact]
    public void ActiveVideoStateWaitsInsteadOfRestartingImageOrVideoStage()
    {
        var decision = RVideoRules.Evaluate(RVideoExecutionModes.Auto,
            new[] { VideoSceneStatuses.VideoRendering, VideoSceneStatuses.VideoReady }, false);

        Assert.False(decision.ShouldQueueVideo);
        Assert.False(decision.ShouldFinalize);
        Assert.Equal(RVideoStages.Video, decision.Stage);
    }

    [Fact]
    public void ProjectWithImageReadyAndFailedSceneIsNotVideoFinalYet()
    {
        var decision = RVideoRules.Evaluate(RVideoExecutionModes.Auto,
            new[] { VideoSceneStatuses.ImageReady, VideoSceneStatuses.Failed }, false);

        Assert.False(decision.ShouldQueueVideo);
        Assert.False(decision.ShouldFinalize);
        Assert.False(decision.TerminalFailure);
    }

    [Fact]
    public void TerminalImageFailureDoesNotAutoRetry()
    {
        var scene = LifecycleState(imageFailed: true);

        Assert.False(RVideoRules.NeedsImageWork(scene));
    }

    [Fact]
    public void ExplicitImageRetryBecomesRetryable()
    {
        var scene = LifecycleState(imageFailed: true, imageRetryRequested: true);

        Assert.True(RVideoRules.NeedsImageWork(scene));
    }

    [Fact]
    public void ImageFailuresStayAtImageStageAndDoNotFinalize()
    {
        var decision = RVideoRules.Evaluate(RVideoExecutionModes.Auto,
            new[] { LifecycleState(imageFailed: true), LifecycleState(imageFailed: true, sceneId: 2) }, false);

        Assert.False(decision.ShouldFinalize);
        Assert.Equal(RVideoStages.Image, decision.Stage);
    }

    [Fact]
    public void StageAwarePartialVideoFailureFinalizes()
    {
        var decision = RVideoRules.Evaluate(RVideoExecutionModes.Auto,
            new[] { LifecycleState(hasVideo: true), LifecycleState(videoFailed: true, sceneId: 2) }, false);

        Assert.True(decision.ShouldFinalize);
    }

    [Fact]
    public void ImageFailureAndVideoReadyDoesNotFinalize()
    {
        var decision = RVideoRules.Evaluate(RVideoExecutionModes.Auto,
            new[] { LifecycleState(imageFailed: true), LifecycleState(hasVideo: true, sceneId: 2) }, false);

        Assert.False(decision.ShouldFinalize);
        Assert.Equal(RVideoStages.Image, decision.Stage);
    }

    [Fact]
    public void ClassifierUsesPersistedFailureEventToDistinguishImageAndVideoFailure()
    {
        var failedScene = new VideoProjectSceneDto
        {
            Id = 7,
            SceneIndex = 1,
            Status = VideoSceneStatuses.Failed
        };
        var imageFailure = RVideoSceneLifecycleClassifier.Classify(failedScene, new[]
        {
            new VideoProjectEventDto
            {
                Id = 1,
                EventType = "SCENE_IMAGE_RENDER_FAILED",
                DataJson = """{"sceneId":7}""",
                CreatedAt = DateTime.UtcNow
            }
        });
        var videoFailure = RVideoSceneLifecycleClassifier.Classify(failedScene, new[]
        {
            new VideoProjectEventDto
            {
                Id = 2,
                EventType = "SCENE_VIDEO_RENDER_FAILED",
                DataJson = """{"sceneId":7}""",
                CreatedAt = DateTime.UtcNow
            }
        });

        Assert.True(imageFailure.ImageFailedTerminal);
        Assert.False(imageFailure.VideoFailedTerminal);
        Assert.True(videoFailure.VideoFailedTerminal);
        Assert.False(videoFailure.ImageFailedTerminal);
    }

    [Fact]
    public void ImageReadyAndVideoReadyQueuesOnlyPendingVideo()
    {
        var states = new[]
        {
            LifecycleState(hasImage: true),
            LifecycleState(hasVideo: true, sceneId: 2)
        };
        var decision = RVideoRules.Evaluate(RVideoExecutionModes.Auto, states, false);

        Assert.True(decision.ShouldQueueVideo);
        Assert.Equal(new[] { 1L }, states.Where(x => x.IsImageReady).Select(x => x.SceneId));
    }

    [Fact]
    public void ActiveVideoAndImageFailureDoesNotRequestImageWork()
    {
        var states = new[]
        {
            LifecycleState(videoAttemptActive: true),
            LifecycleState(imageFailed: true, sceneId: 2)
        };
        var decision = RVideoRules.Evaluate(RVideoExecutionModes.Auto, states, false);

        Assert.DoesNotContain(states, RVideoRules.NeedsImageWork);
        Assert.False(decision.ShouldQueueVideo);
        Assert.False(decision.ShouldFinalize);
    }

    [Fact]
    public void FinalDurationUsesMergeableScenesOnly()
    {
        var duration = RVideoRules.CalculateMergedDuration(new[]
        {
            new VideoProjectSceneDto { DurationSeconds = 8, Status = VideoSceneStatuses.VideoReady },
            new VideoProjectSceneDto { DurationSeconds = 8, Status = VideoSceneStatuses.Failed },
            new VideoProjectSceneDto { DurationSeconds = 6, Status = VideoSceneStatuses.VideoReady }
        }.Where(x => x.Status == VideoSceneStatuses.VideoReady));

        Assert.Equal(14m, duration);
    }

    [Fact]
    public void TenantGuardRejectsForeignOrMissingProject()
    {
        var tenant = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => RVideoRules.EnsureProjectOwnership(Guid.NewGuid(), tenant));
        Assert.Throws<InvalidOperationException>(() => RVideoRules.EnsureProjectOwnership(null, tenant));
        RVideoRules.EnsureProjectOwnership(tenant, tenant);
    }

    [Fact]
    public void ManualLifecycleStopsAfterImageTerminal()
    {
        var decision = RVideoRules.Evaluate(RVideoExecutionModes.Manual,
            new[] { VideoSceneStatuses.ImageReady }, false);

        Assert.Equal(RVideoStages.Image, decision.Stage);
        Assert.False(decision.ShouldQueueVideo);
        Assert.False(decision.ShouldFinalize);
    }

    [Fact]
    public void StageAwareManualLifecycleDoesNotQueueVideoOrFinalize()
    {
        var decision = RVideoRules.Evaluate(RVideoExecutionModes.Manual,
            new[] { LifecycleState(hasImage: true) }, false);

        Assert.False(decision.ShouldQueueVideo);
        Assert.False(decision.ShouldFinalize);
    }

    [Fact]
    public void RVideoServiceResolvesToNativeCreateRoute()
    {
        var serviceId = Guid.NewGuid();
        var route = CustomerServiceRouting.Resolve(TodoXServiceEngineTypes.RVideo, serviceId, "BUDDHISM_CONTENT_VIDEO");

        Assert.Equal(CustomerServiceDestination.RVideoCreator, route.Destination);
        Assert.Equal("/jobs/rvideo/new?serviceId=" + serviceId + "&serviceCode=BUDDHISM_CONTENT_VIDEO", route.Route);
        Assert.Null(route.Message);
    }

    [Fact]
    public void TimelapseAndRDanceRoutesRemainUnchanged()
    {
        Assert.StartsWith("/jobs/timelapse/new?", CustomerServiceRouting.Resolve(TodoXServiceEngineTypes.Timelapse, Guid.NewGuid(), "CONSTRUCTION_VIDEO").Route);
        Assert.StartsWith("/jobs/rdance/new?", CustomerServiceRouting.Resolve(TodoXServiceEngineTypes.RDance, Guid.NewGuid(), "FASHION_VIDEO").Route);
    }

    [Fact]
    public void UnknownServiceEngineStillUsesComingSoonFallback()
    {
        var route = CustomerServiceRouting.Resolve("future-engine");

        Assert.Equal(CustomerServiceDestination.Unavailable, route.Destination);
        Assert.Null(route.Route);
        Assert.False(string.IsNullOrWhiteSpace(route.Message));
    }

    [Fact]
    public void SceneDurationAndLibraryVoiceAreValidated()
    {
        Assert.Throws<InvalidOperationException>(() => RVideoRules.ValidateScene(
            new RVideoSceneEditorItem(1, "Hook", 5, "prompt", null, null, null, null, null)));
        Assert.Throws<InvalidOperationException>(() => RVideoRules.ValidateSettings(new RVideoJobSettingsRequest
        {
            SkipCharacter = true,
            VoiceMode = RVideoVoiceModes.Library
        }));
    }

    [Theory]
    [InlineData("16:9", "1080p")]
    [InlineData("9:16", "720p")]
    public void AutoRenderSettingsPreservePersistedAspectRatioAndResolution(string ratio, string resolution)
    {
        var resolved = RVideoRules.ResolveRenderSettings($$"""
        {"aspect_ratio":"{{ratio}}","resolution":"{{resolution}}"}
        """);

        Assert.Equal(ratio, resolved.AspectRatio);
        Assert.Equal(resolution, resolved.Resolution);
    }

    [Fact]
    public void SkipCharacterClearsSelectionAndSnapshot()
    {
        var request = new RVideoJobSettingsRequest
        {
            SkipCharacter = true,
            CharacterMode = RVideoCharacterModes.Library,
            SelectedCharacterId = 42,
            CharacterSnapshot = new { id = 42 }
        };

        RVideoRules.ValidateSettings(request);

        Assert.Equal(RVideoCharacterModes.None, request.CharacterMode);
        Assert.Null(request.SelectedCharacterId);
        Assert.Null(request.CharacterSnapshot);
    }

    [Fact]
    public void UploadAndLibraryCharacterModesRequireRuntimeState()
    {
        Assert.Throws<InvalidOperationException>(() => RVideoRules.ValidateSettings(new RVideoJobSettingsRequest
        {
            CharacterMode = RVideoCharacterModes.Upload
        }));
        Assert.Throws<InvalidOperationException>(() => RVideoRules.ValidateSettings(new RVideoJobSettingsRequest
        {
            CharacterMode = RVideoCharacterModes.Library,
            SelectedCharacterId = 1
        }));
    }

    [Fact]
    public void MusicValidationRequiresLocalActiveMp3()
    {
        var valid = new AiStudioMusicDto
        {
            Code = "demo",
            Name = "Demo",
            IsActive = true,
            FileName = "demo.mp3",
            StorageKey = "music/demo.mp3",
            FileUrl = "/uploads/music/demo.mp3",
            MimeType = "audio/mpeg"
        };
        var request = new RVideoJobSettingsRequest
        {
            SkipCharacter = true,
            MusicCatalogCode = "demo",
            MusicSnapshot = new { code = "demo" }
        };

        RVideoRules.ValidateSettings(request);
        RVideoRules.ValidateActiveMusic(valid, request);
        valid.IsActive = false;
        Assert.Throws<InvalidOperationException>(() => RVideoRules.ValidateActiveMusic(valid, request));
    }

    private static RVideoSceneLifecycleState LifecycleState(
        long sceneId = 1,
        bool hasImage = false,
        bool hasVideo = false,
        bool imageFailed = false,
        bool videoFailed = false,
        bool videoAttemptActive = false,
        bool imageRetryRequested = false)
        => new(
            sceneId,
            (int)sceneId,
            8,
            hasImage,
            hasVideo,
            ImageAttemptActive: false,
            imageFailed,
            videoAttemptActive,
            videoFailed,
            imageRetryRequested);
}
