using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using System.Text.Json;
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
    public void SharedReferenceImageCountsAsImageReady()
    {
        var state = RVideoSceneLifecycleClassifier.Classify(
            new VideoProjectSceneDto { Id = 99, SceneIndex = 1, Status = VideoSceneStatuses.Draft },
            usesSharedReferenceImage: true);

        Assert.True(state.HasImage);
        Assert.True(state.UsesSharedReferenceImage);
        Assert.False(RVideoRules.NeedsImageWork(state));
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
    public void RVideoCreatedResultUsesCoreJobUuidRoute()
    {
        var jobId = Guid.NewGuid();
        var result = new RVideoJobCreatedResult(jobId, 42, "draft", $"/jobs/rvideo/{jobId}");

        Assert.Equal(jobId, result.JobId);
        Assert.Equal($"/jobs/rvideo/{jobId}", result.Route);
        Assert.DoesNotContain("projectId=", result.Route, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NewRVideoSettingsDefaultToManualInfoWithoutBillingFields()
    {
        var settings = new RVideoJobSettingsRequest();

        Assert.Equal(RVideoExecutionModes.Manual, settings.ExecutionMode);
        Assert.Equal(RVideoVoiceModes.None, settings.VoiceMode);
        Assert.Equal(1.0m, settings.DefaultTtsRate);
        Assert.False(settings.UseReferenceImageForAllScenes);
    }

    [Fact]
    public void ToRequestCarriesSharedReferenceImageSetting()
    {
        var request = RVideoRules.ToRequest(new RVideoJobSettingsDto
        {
            UseReferenceImageForAllScenes = true
        });

        Assert.True(request.UseReferenceImageForAllScenes);
    }

    [Fact]
    public void SharedReferenceImageResolverPrefersSharedReferenceSource()
    {
        var settings = new RVideoJobSettingsDto
        {
            UseReferenceImageForAllScenes = true,
            SkipCharacter = false,
            CharacterMode = RVideoCharacterModes.Library,
            SelectedCharacterId = 42,
            CharacterSnapshotJson = """
                {
                  "source": "LIBRARY",
                  "id": 42,
                  "masterImageUrl": "https://example.invalid/reference.jpg",
                  "storageKey": "ref-key",
                  "normalizedPrompt": "consistent reference"
                }
                """
        };

        var source = RVideoEffectiveSceneImageSourceResolver.Resolve(
            new VideoProjectSceneDto { Id = 1, SceneIndex = 1, Status = VideoSceneStatuses.Draft },
            settings,
            selectedImageVersion: null);

        Assert.True(source.UsesSharedReferenceImage);
        Assert.Equal("https://example.invalid/reference.jpg", source.SourceImageUrl);
        Assert.Equal("ref-key", source.SourceImageObjectKey);
        Assert.Null(source.SelectedImageVersionId);
        Assert.True(source.HasUsableInput);
        Assert.Equal("Ảnh tham khảo dùng chung", source.SourceLabel);
    }

    [Fact]
    public void DirectProjectSourceImageResolverPrefersProjectSourceImage()
    {
        var source = RVideoEffectiveSceneImageSourceResolver.Resolve(
            new VideoProjectSceneDto { Id = 1, SceneIndex = 1, Status = VideoSceneStatuses.Draft },
            settings: null,
            selectedImageVersion: null,
            project: new VideoProjectDto
            {
                Id = 99,
                SourceImageUrl = "https://example.invalid/source.jpg"
            });

        Assert.False(source.UsesSharedReferenceImage);
        Assert.Equal("https://example.invalid/source.jpg", source.SourceImageUrl);
        Assert.Equal(RVideoEffectiveSceneImageSourceResolver.ProjectSourceImage, source.SourceLabel);
        Assert.Null(source.SelectedImageVersionId);
    }

    [Fact]
    public void SelectedAiImageStillTakesPriorityOverProjectSourceImage()
    {
        var selectedImageVersionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var source = RVideoEffectiveSceneImageSourceResolver.Resolve(
            new VideoProjectSceneDto
            {
                Id = 1,
                SceneIndex = 1,
                Status = VideoSceneStatuses.Draft,
                StaticImageUrl = "https://example.invalid/static.jpg"
            },
            settings: null,
            selectedImageVersion: new SceneImageVersionDto
            {
                Id = selectedImageVersionId,
                Status = "completed",
                PublicUrl = "https://example.invalid/selected.jpg",
                StorageKey = "scene/selected.jpg"
            },
            project: new VideoProjectDto
            {
                Id = 99,
                SourceImageUrl = "https://example.invalid/source.jpg"
            });

        Assert.Equal(selectedImageVersionId, source.SelectedImageVersionId);
        Assert.Equal("https://example.invalid/selected.jpg", source.SourceImageUrl);
        Assert.Equal("scene/selected.jpg", source.SourceImageObjectKey);
        Assert.Equal(RVideoEffectiveSceneImageSourceResolver.SceneImageVersion, source.SourceLabel);
    }

    [Fact]
    public void StaticSceneImageStillTakesPriorityOverProjectSourceImage()
    {
        var source = RVideoEffectiveSceneImageSourceResolver.Resolve(
            new VideoProjectSceneDto
            {
                Id = 1,
                SceneIndex = 1,
                Status = VideoSceneStatuses.Draft,
                StaticImageUrl = "https://example.invalid/static.jpg"
            },
            settings: null,
            selectedImageVersion: null,
            project: new VideoProjectDto
            {
                Id = 99,
                SourceImageUrl = "https://example.invalid/source.jpg"
            });

        Assert.Equal("https://example.invalid/static.jpg", source.SourceImageUrl);
        Assert.Equal(RVideoEffectiveSceneImageSourceResolver.SceneStaticImage, source.SourceLabel);
    }

    [Fact]
    public void MissingAllSourceImagesIsReported()
    {
        var source = RVideoEffectiveSceneImageSourceResolver.Resolve(
            new VideoProjectSceneDto { Id = 1, SceneIndex = 1, Status = VideoSceneStatuses.Draft },
            settings: null,
            selectedImageVersion: null,
            project: new VideoProjectDto { Id = 99 });

        Assert.False(source.HasUsableInput);
        Assert.Equal(RVideoEffectiveSceneImageSourceResolver.Missing, source.SourceLabel);
        Assert.Null(source.SourceImageUrl);
    }

    [Fact]
    public void SceneVideoRenderInputCarriesSharedReferenceImageSelection()
    {
        var reference = new RVideoSceneImageReferenceSelection(
            true,
            CharacterId: 42,
            ObjectKey: "references/shared.png",
            Url: "https://example.invalid/shared.png",
            CharacterPrompt: "consistent character",
            Source: RVideoSceneImageReferenceSelection.LibrarySource);

        var input = new SceneVideoRenderInput
        {
            ProjectId = 100,
            SceneIds = new[] { 200L },
            AspectRatio = "9:16",
            Resolution = "720p"
        };

        input.ApplySharedReferenceImage(reference);

        Assert.True(input.UseSharedReferenceImage);
        Assert.Equal("https://example.invalid/shared.png", input.SharedReferenceImageUrl);
        Assert.Equal("references/shared.png", input.SharedReferenceImageObjectKey);
    }

    [Fact]
    public void SceneVideoRenderInputRejectsMissingSharedReferenceImage()
    {
        var input = new SceneVideoRenderInput();

        var ex = Assert.Throws<InvalidOperationException>(() => input.ApplySharedReferenceImage(null, null));

        Assert.Equal("RVIDEO_SHARED_REFERENCE_IMAGE_REQUIRED", ex.Message);
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

    [Fact]
    public void RequiresExternalVoiceUsesLibraryModeAndNarrationText()
    {
        var scene = new VideoProjectSceneDto
        {
            ScenePrompt = new ScenePromptMetadata
            {
                Voice = "Read this line"
            }.Serialize()
        };
        var settings = new RVideoJobSettingsDto
        {
            VoiceMode = RVideoVoiceModes.Library
        };

        Assert.True(RVideoRules.RequiresExternalVoice(scene, settings));
        Assert.False(RVideoRules.RequiresExternalVoice(scene, new RVideoJobSettingsDto { VoiceMode = RVideoVoiceModes.None }));
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
    public void UploadSnapshotMapsToSceneImageBatchReferenceWithoutCharacterId()
    {
        var settings = new RVideoJobSettingsDto
        {
            SkipCharacter = false,
            CharacterMode = RVideoCharacterModes.Upload,
            CharacterSnapshotJson = """
                {
                  "source": "UPLOAD",
                  "fileUrl": "/uploads/rvideo_character/202608/character.jpg",
                  "storageKey": "rvideo_character/202608/character.jpg"
                }
                """
        };

        var input = RVideoSceneImageReferenceSelection.BuildBatchInput(settings);

        Assert.Equal(RVideoSceneImageReferenceSelection.UploadSource, input.ReferenceSource);
        Assert.Null(input.CharacterId);
        Assert.Equal("/uploads/rvideo_character/202608/character.jpg", input.CharacterReferenceUrl);
        Assert.Equal("rvideo_character/202608/character.jpg", input.CharacterReferenceObjectKey);
    }

    [Fact]
    public void CamelCaseUploadedSnapshotDeserializesIntoUploadedCharacterSnapshot()
    {
        var snapshot = JsonSerializer.Deserialize<UploadedCharacterSnapshot>(
            """
            {
              "source": "UPLOAD",
              "fileUrl": "/uploads/rvideo_character/202608/character.jpg",
              "fileName": "character.jpg",
              "storageKey": "rvideo_character/202608/character.jpg"
            }
            """,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            });

        Assert.NotNull(snapshot);
        Assert.Equal("UPLOAD", snapshot!.Source);
        Assert.Equal("/uploads/rvideo_character/202608/character.jpg", snapshot.FileUrl);
        Assert.Equal("character.jpg", snapshot.FileName);
        Assert.Equal("rvideo_character/202608/character.jpg", snapshot.StorageKey);
    }

    [Fact]
    public void ValidPersistedUploadSnapshotSurvivesEmptyUiSaveRequest()
    {
        var request = new RVideoJobSettingsRequest
        {
            SkipCharacter = false,
            CharacterMode = RVideoCharacterModes.Upload,
            CharacterSnapshot = new UploadedCharacterSnapshot("UPLOAD", "", "", "")
        };
        var persisted = new RVideoJobSettingsDto
        {
            SkipCharacter = false,
            CharacterMode = RVideoCharacterModes.Upload,
            CharacterSnapshotJson = """
                {
                  "source": "UPLOAD",
                  "fileUrl": "/uploads/rvideo_character/202608/character.jpg",
                  "fileName": "character.jpg",
                  "storageKey": "rvideo_character/202608/character.jpg"
                }
                """
        };

        RVideoRules.PreserveValidUploadedCharacterSnapshot(request, persisted);
        Assert.NotNull(request.CharacterSnapshot);
        var input = RVideoSceneImageReferenceSelection.BuildBatchInput(new RVideoJobSettingsDto
        {
            SkipCharacter = request.SkipCharacter,
            CharacterMode = request.CharacterMode,
            CharacterSnapshotJson = JsonSerializer.Serialize(request.CharacterSnapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });

        Assert.NotNull(request.CharacterSnapshot);
        Assert.Equal(RVideoSceneImageReferenceSelection.UploadSource, input.ReferenceSource);
        Assert.Null(input.CharacterId);
        Assert.Equal("/uploads/rvideo_character/202608/character.jpg", input.CharacterReferenceUrl);
        Assert.Equal("rvideo_character/202608/character.jpg", input.CharacterReferenceObjectKey);
    }

    [Fact]
    public void PerSceneRerenderUploadReferenceUsesPersistedSnapshotWithoutCharacterId()
    {
        var settings = new RVideoJobSettingsDto
        {
            SkipCharacter = false,
            CharacterMode = RVideoCharacterModes.Upload,
            CharacterSnapshotJson = """
                {
                  "source": "UPLOAD",
                  "fileUrl": "/uploads/rvideo_character/202608/character.jpg",
                  "storageKey": "rvideo_character/202608/character.jpg"
                }
                """
        };

        var reference = RVideoSceneImageReferenceSelection.Resolve(settings);

        Assert.Equal(RVideoSceneImageReferenceSelection.UploadSource, reference.Source);
        Assert.True(reference.ReferenceRequested);
        Assert.Null(reference.CharacterId);
        Assert.Equal("/uploads/rvideo_character/202608/character.jpg", reference.Url);
        Assert.Equal("rvideo_character/202608/character.jpg", reference.ObjectKey);
    }

    [Fact]
    public void UploadModeRequiresSnapshotReferenceBeforeEnqueue()
    {
        var settings = new RVideoJobSettingsDto
        {
            SkipCharacter = false,
            CharacterMode = RVideoCharacterModes.Upload,
            CharacterSnapshotJson = """{"source":"UPLOAD"}"""
        };

        var ex = Assert.Throws<InvalidOperationException>(() => RVideoSceneImageReferenceSelection.Resolve(settings));

        Assert.Equal("RVVIDEO_UPLOADED_CHARACTER_REFERENCE_UNAVAILABLE", ex.Message);
    }

    [Fact]
    public void InvalidUploadSnapshotIsNotPreservedForManualRender()
    {
        var request = new RVideoJobSettingsRequest
        {
            SkipCharacter = false,
            CharacterMode = RVideoCharacterModes.Upload,
            CharacterSnapshot = null
        };
        var persisted = new RVideoJobSettingsDto
        {
            SkipCharacter = false,
            CharacterMode = RVideoCharacterModes.Upload,
            CharacterSnapshotJson = """{"source":"UPLOAD","fileUrl":null,"storageKey":null}"""
        };

        RVideoRules.PreserveValidUploadedCharacterSnapshot(request, persisted);

        var ex = Assert.Throws<InvalidOperationException>(() => RVideoRules.ValidateSettings(request));
        Assert.Equal("RVVIDEO_UPLOADED_CHARACTER_REQUIRED", ex.Message);
    }

    [Fact]
    public void LibrarySnapshotKeepsSelectedCharacterAndMediaReference()
    {
        var settings = new RVideoJobSettingsDto
        {
            SkipCharacter = false,
            CharacterMode = RVideoCharacterModes.Library,
            SelectedCharacterId = 42,
            CharacterSnapshotJson = """
                {
                  "source": "LIBRARY",
                  "id": 42,
                  "masterImageUrl": "/uploads/characters/master.jpg",
                  "storageKey": "characters/master.jpg",
                  "normalizedPrompt": "consistent hero"
                }
                """
        };

        var reference = RVideoSceneImageReferenceSelection.Resolve(settings);

        Assert.Equal(RVideoSceneImageReferenceSelection.LibrarySource, reference.Source);
        Assert.Equal(42, reference.CharacterId);
        Assert.Equal("/uploads/characters/master.jpg", reference.Url);
        Assert.Equal("characters/master.jpg", reference.ObjectKey);
        Assert.Equal("consistent hero", reference.CharacterPrompt);
    }

    [Fact]
    public void NoneModeDoesNotRequestSceneImageReference()
    {
        var settings = new RVideoJobSettingsDto
        {
            SkipCharacter = true,
            CharacterMode = RVideoCharacterModes.None
        };

        var reference = RVideoSceneImageReferenceSelection.Resolve(settings);

        Assert.False(reference.ReferenceRequested);
        Assert.Equal(RVideoSceneImageReferenceSelection.NoneSource, reference.Source);
        Assert.Null(reference.CharacterId);
        Assert.Null(reference.Url);
        Assert.Null(reference.ObjectKey);
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
            false,
            hasVideo,
            false,
            imageFailed,
            videoAttemptActive,
            videoFailed,
            imageRetryRequested);
}
