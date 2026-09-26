using System.Reflection;
using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoAudioRecoveryTests
{
    [Fact]
    public async Task CompletedVideoWithoutAudioInvokesAudioAutoChainOnly()
    {
        var project = Project(Scene(545, 1, 1.85m));
        var invoked = new List<long>();

        var result = await RVideoAudioRecoveryService.RecoverEligibleScenesAsync(
            project,
            LibrarySettings(),
            scene =>
            {
                invoked.Add(scene.Id);
                return Task.FromResult(true);
            });

        Assert.Equal(new long[] { 545 }, invoked);
        Assert.Equal(1, result.EnqueueRequested);
        Assert.Equal("video_ready", project.Scenes[0].Status);
        Assert.Equal("https://cdn.example/video.mp4", project.Scenes[0].SceneVideoUrl);
    }

    [Fact]
    public async Task NoneAndNativeVoiceModesDoNotInvokeExternalAudioRecovery()
    {
        foreach (var mode in new[] { RVideoVoiceModes.None, RVideoVoiceModes.Native })
        {
            var calls = 0;
            var result = await RVideoAudioRecoveryService.RecoverEligibleScenesAsync(
                Project(Scene(1, 1, 1m)),
                new RVideoJobSettingsDto { VoiceMode = mode },
                _ =>
                {
                    calls++;
                    return Task.FromResult(true);
                });

            Assert.Equal(0, calls);
            Assert.Equal(0, result.EligibleScenes);
            Assert.Equal("skipped_not_external_voice", result.Scenes[0].Action);
        }
    }

    [Fact]
    public async Task MultiSceneFailureDoesNotStopRemainingScenes()
    {
        var project = Project(Scene(545, 1, 1.85m), Scene(546, 2, 2m), Scene(547, 3, 1.6m));
        var invoked = new List<long>();

        var result = await RVideoAudioRecoveryService.RecoverEligibleScenesAsync(
            project,
            LibrarySettings(),
            scene =>
            {
                invoked.Add(scene.Id);
                return scene.Id == 546
                    ? throw new InvalidOperationException("RVIDEO_TTS_RATE_OUT_OF_RANGE")
                    : Task.FromResult(true);
            });

        Assert.Equal(new long[] { 545, 546, 547 }, invoked);
        Assert.Equal(2, result.EnqueueRequested);
        Assert.Equal(1, result.FailedScenes);
        Assert.Equal("RVIDEO_TTS_RATE_OUT_OF_RANGE", result.Scenes[1].ErrorCode);
    }

    [Theory]
    [InlineData(0.8, 0.8, 2.0)]
    [InlineData(1.0, 0.8, 2.0)]
    [InlineData(2.0, 0.8, 2.0)]
    public void AudioChainRateValidationIncludesCatalogBoundaries(double rate, double min, double max)
    {
        InvokeRateValidation((decimal)rate, (decimal)min, (decimal)max);
    }

    [Theory]
    [InlineData(0.79, 0.8, 2.0)]
    [InlineData(2.01, 0.8, 2.0)]
    public void AudioChainRateValidationRejectsValuesOutsideCatalog(double rate, double min, double max)
    {
        var ex = Assert.Throws<TargetInvocationException>(() => InvokeRateValidation((decimal)rate, (decimal)min, (decimal)max));
        Assert.Equal("RVIDEO_TTS_RATE_OUT_OF_RANGE", ex.InnerException?.Message);
    }

    [Fact]
    public void EndpointIsAuthenticatedTenantScopedAndAudioOnly()
    {
        var endpoint = ReadRepoFile("Services", "VideoRender", "RVideoEndpoints.cs");
        var service = ReadRepoFile("Services", "VideoRender", "RVideoAudioRecoveryService.cs");

        Assert.Contains("/projects/{projectId:long}/recover-audio", endpoint, StringComparison.Ordinal);
        Assert.Contains("RequireUserAsync", endpoint, StringComparison.Ordinal);
        Assert.Contains("GetProjectAsync(projectId, user, ct)", service, StringComparison.Ordinal);
        Assert.Contains("TryEnqueueSceneAudioAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("TryEnqueueSceneVideoAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("SceneImage", service, StringComparison.Ordinal);
        Assert.DoesNotContain("79Ai", service, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedAudioChainRetainsConcurrencyAndCompletedAudioGuards()
    {
        var chain = ReadRepoFile("Services", "VideoRender", "RVideoSceneAudioAutoChainService.cs");
        var versions = ReadRepoFile("Services", "VideoRender", "SceneMediaVersioningService.cs");
        var jobs = ReadRepoFile("Services", "Render", "RenderJobService.cs");

        Assert.Contains("GetSelectedAudioVersionAsync(sceneId, ct)", chain, StringComparison.Ordinal);
        Assert.Contains("HasActiveAudioVersionAsync(sceneId, ct)", chain, StringComparison.Ordinal);
        Assert.Contains("BuildLogicalRequestKey(projectId, sceneId)", chain, StringComparison.Ordinal);
        Assert.Contains("LockSceneAsync(conn, tx, request.ProjectId, request.SceneId", versions, StringComparison.Ordinal);
        Assert.Contains("WHERE logical_request_id=@logicalRequestId AND tenant_id=@tenant", versions, StringComparison.Ordinal);
        Assert.Contains("pg_advisory_xact_lock", jobs, StringComparison.Ordinal);
        Assert.Contains("BuildLogCodeJobLockName(jobType, uniqueLogCode)", jobs, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletedSelectedAudioSkipsBeforeAnyNewVersionOrProviderJob()
    {
        var chain = ReadRepoFile("Services", "VideoRender", "RVideoSceneAudioAutoChainService.cs");
        var completedGuard = chain.IndexOf("GetSelectedAudioVersionAsync(sceneId, ct)", StringComparison.Ordinal);
        var versionCreation = chain.IndexOf("CreateQueuedSceneAudioVersionAsync", StringComparison.Ordinal);

        Assert.True(completedGuard >= 0);
        Assert.True(versionCreation > completedGuard);
        Assert.Contains("selected.Status, \"completed\"", chain[completedGuard..versionCreation], StringComparison.Ordinal);
        Assert.Contains("return false", chain[completedGuard..versionCreation], StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveAudioAndRepeatedRecoveryUseExistingLogicalRequestContract()
    {
        var chain = ReadRepoFile("Services", "VideoRender", "RVideoSceneAudioAutoChainService.cs");

        Assert.Contains("GetSceneAudioVersionByLogicalRequestIdAsync(logicalRequestId, ct)", chain, StringComparison.Ordinal);
        Assert.Contains("HasActiveAudioVersionAsync(sceneId, ct)", chain, StringComparison.Ordinal);
        Assert.Contains("EnqueueForLogCodeIfNoneActiveAsync(model, logicalRequestId, ct)", chain, StringComparison.Ordinal);
        Assert.Contains("rvideo-scene-audio:{projectId}:{sceneId}", chain, StringComparison.Ordinal);
    }

    [Fact]
    public void AudioCompletionContinuesThroughExistingMuxAndFinalMergeServices()
    {
        var audioHandler = ReadRepoFile("Services", "VideoRender", "SceneAudioRenderHandler.cs");
        var muxHandler = ReadRepoFile("Services", "VideoRender", "SceneAudioMuxHandler.cs");

        Assert.Contains("TryFinalizeSceneMediaAsync(project.Id, scene.Id, \"SCENE_AUDIO_READY\", ct)", audioHandler, StringComparison.Ordinal);
        Assert.Contains("TryEnqueueFinalMergeAsync(project.Id, RVideoProjectFinalizationContracts.TriggerSceneAudioReady, ct)", muxHandler, StringComparison.Ordinal);
    }

    private static void InvokeRateValidation(decimal rate, decimal min, decimal max)
    {
        var method = typeof(RVideoSceneAudioAutoChainService).GetMethod("ValidateTtsRate", BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Invoke(null, new object[]
        {
            new AiStudioVoiceDto { MinRate = min, MaxRate = max },
            rate
        });
    }

    private static RVideoJobSettingsDto LibrarySettings()
        => new() { VoiceMode = RVideoVoiceModes.Library, VoiceCatalogCode = "voice-code", DefaultTtsRate = 1m };

    private static VideoProjectDto Project(params VideoProjectSceneDto[] scenes)
        => new() { Id = 112, Scenes = scenes.ToList() };

    private static VideoProjectSceneDto Scene(long id, int index, decimal rate)
        => new()
        {
            Id = id,
            ProjectId = 112,
            SceneIndex = index,
            Status = VideoSceneStatuses.VideoReady,
            SceneVideoUrl = "https://cdn.example/video.mp4",
            VoiceEnabled = true,
            VoiceText = "Narration",
            ScenePrompt = $"voice: Narration | tts_rate: {rate.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
        };

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "TodoX.Web" }.Concat(parts).ToArray()));
}
