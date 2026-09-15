using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using TodoX.Web.Services.ImageRender;
using TodoX.Web.Services.Media;
using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class VbeeSceneRuntimeTests
{
    [Fact]
    public void LocalMediaPathResolver_UsesAbsoluteSourceFilePathWhenItExists()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            Assert.True(LocalMediaPathResolver.TryResolveExistingFile(
                tempFile,
                LocalMediaPathSource.SourceFilePath,
                AppContext.BaseDirectory,
                "wwwroot/uploads",
                "/uploads",
                out var resolved));

            Assert.Equal(Path.GetFullPath(tempFile), resolved);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LocalMediaPathResolver_ResolvesRelativeSourceFilePathUnderUploadRoot()
    {
        var contentRoot = CreateTempContentRoot(out var audioPath);
        try
        {
            Assert.True(LocalMediaPathResolver.TryResolveExistingFile(
                "render-projects/a/b/scene-audio.mp3",
                LocalMediaPathSource.SourceFilePath,
                contentRoot,
                "wwwroot/uploads",
                "/uploads",
                out var resolved));

            Assert.Equal(audioPath, resolved);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task MuxAudioResolver_FallsBackToStorageKeyResultMediaAndLocalPublicUrl()
    {
        var contentRoot = CreateTempContentRoot(out var audioPath);
        var resolver = CreateResolver(contentRoot);
        var mediaId = Guid.NewGuid();
        var media = new StubMediaFileService(new MediaFileDto
        {
            Id = mediaId,
            ObjectKey = "render-projects/a/b/scene-audio.mp3",
            PublicUrl = "/uploads/render-projects/a/b/scene-audio.mp3"
        });
        try
        {
            var fromStorageKey = await SceneAudioMuxHandler.ResolveSceneAudioPathAsync(
                new SceneAudioVersionDto { StorageKey = "render-projects/a/b/scene-audio.mp3" },
                resolver,
                media,
                CancellationToken.None);
            var fromMedia = await SceneAudioMuxHandler.ResolveSceneAudioPathAsync(
                new SceneAudioVersionDto { ResultMediaId = mediaId },
                resolver,
                media,
                CancellationToken.None);
            var fromPublicUrl = await SceneAudioMuxHandler.ResolveSceneAudioPathAsync(
                new SceneAudioVersionDto { PublicUrl = "/uploads/render-projects/a/b/scene-audio.mp3" },
                resolver,
                media,
                CancellationToken.None);

            Assert.Equal(audioPath, fromStorageKey);
            Assert.Equal(audioPath, fromMedia);
            Assert.Equal(audioPath, fromPublicUrl);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void LocalMediaPathResolver_RejectsTraversalOutsideUploadRoot()
    {
        var contentRoot = CreateTempContentRoot(out _);
        try
        {
            Assert.False(LocalMediaPathResolver.TryResolveExistingFile(
                "../../secret",
                LocalMediaPathSource.StorageKey,
                contentRoot,
                "wwwroot/uploads",
                "/uploads",
                out var resolved));

            Assert.Equal(string.Empty, resolved);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void SceneAudioMuxHandler_ResolvesAudioBeforeReportingMissingAndNeverCallsVbee()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneAudioMuxHandler.cs");
        var handlerStart = source.IndexOf("public async Task HandleAsync", StringComparison.Ordinal);
        var audioResolveIndex = source.IndexOf("ResolveSceneAudioPathAsync(sceneAudio", handlerStart, StringComparison.Ordinal);
        var missingIndex = source.IndexOf("Source scene audio is missing", handlerStart, StringComparison.Ordinal);

        Assert.True(audioResolveIndex >= 0);
        Assert.True(missingIndex > audioResolveIndex);
        Assert.DoesNotContain("IVbeeVoiceClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmitAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneAudioMuxHandler_SkipsRepeatedCompletedMuxOutput()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneAudioMuxHandler.cs");
        var finalizer = ReadRepoFile("Services", "VideoRender", "RVideoSceneMediaFinalizerService.cs");

        Assert.Contains("sceneVideo.VoiceAudioVersionId == sceneAudio.Id", source, StringComparison.Ordinal);
        Assert.Contains("IsCompletedMuxOutput(sceneVideo)", source, StringComparison.Ordinal);
        Assert.Contains("LocalMediaPathResolver", finalizer, StringComparison.Ordinal);
        Assert.Contains("TryResolveExistingFile", finalizer, StringComparison.Ordinal);
    }

    [Fact]
    public void MuxCompletion_UsesFinalPath_AndVoiceLinkage()
    {
        var sceneVideo = new SceneVideoVersionDto
        {
            PosterUrl = "https://cdn.example.com/poster.jpg",
            DurationSeconds = 12,
            ProviderCode = "vbee",
            ModelName = "voice-01",
            ProviderCapabilityId = 9,
            ProviderTaskId = "task-123",
            BillingLogicalRequestId = "bill-1",
            EstimatedUsd = 1.23m,
            ActualUsd = 1.11m,
            ChargedPoints = 10,
            RefundedPoints = 0,
            CostSource = "configured_tariff",
            AspectRatio = "16:9",
            ResultMediaId = Guid.NewGuid()
        };

        var request = SceneAudioMuxHandler.BuildCompletionRequest(sceneVideo, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "https://cdn.example.com/final.mp4", @"D:\tmp\final.mp4", 14);

        Assert.Equal("https://cdn.example.com/final.mp4", request.VideoUrl);
        Assert.Equal(@"D:\tmp\final.mp4", request.VideoPath);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), request.VoiceAudioVersionId);
        Assert.Equal(12, request.DurationSeconds);
    }

    [Fact]
    public void MuxCompletion_UsesMuxedMediaIdInsteadOfRawMediaId()
    {
        var rawMediaId = Guid.NewGuid();
        var muxedMediaId = Guid.NewGuid();
        var sceneVideo = new SceneVideoVersionDto { ResultMediaId = rawMediaId };

        var request = SceneAudioMuxHandler.BuildCompletionRequest(
            sceneVideo,
            Guid.NewGuid(),
            "/uploads/video-render/final.mp4",
            @"D:\uploads\final.mp4",
            8,
            muxedMediaId);

        Assert.Equal(muxedMediaId, request.ResultMediaId);
        Assert.NotEqual(rawMediaId, request.ResultMediaId);
    }

    [Fact]
    public void Finalizer_SkipsWhenFinalMuxAlreadyExists()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var tempFile = Path.Combine(tempRoot, "project-1", "final-scenes", "01", "final.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(tempFile)!);
        File.WriteAllText(tempFile, "muxed");
        try
        {
            var video = new SceneVideoVersionDto
            {
                VoiceAudioVersionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                PublicUrl = "https://cdn.example.com/final.mp4",
                SourceFilePath = tempFile
            };
            var audio = new SceneAudioVersionDto
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
            };

            Assert.True(RVideoSceneMediaFinalizerService.ShouldSkipMux(video, audio, AppContext.BaseDirectory, string.Empty));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Finalizer_DoesNotSkipRawVideoStampedWithAudio()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var audioId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            var video = new SceneVideoVersionDto
            {
                VoiceAudioVersionId = audioId,
                PublicUrl = "https://cdn.example.com/raw.mp4",
                SourceFilePath = tempFile
            };
            var audio = new SceneAudioVersionDto { Id = audioId };

            Assert.False(RVideoSceneMediaFinalizerService.ShouldSkipMux(video, audio, AppContext.BaseDirectory, string.Empty));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void MuxCompletion_RejectsCrossSceneAudio()
    {
        var sceneVideo = new SceneVideoVersionDto { ProjectId = 11, SceneId = 101 };
        var sceneAudio = new SceneAudioVersionDto { ProjectId = 11, SceneId = 202 };

        var ex = Assert.Throws<InvalidOperationException>(() => SceneAudioMuxHandler.ValidateSceneOwnership(101, sceneVideo, sceneAudio));

        Assert.Equal("SCENE_AUDIO_MUX_SCENE_ID_MISMATCH", ex.Message);
    }

    [Fact]
    public void CallbackAuthorization_UsesHeaderOrQuerySecret()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-VBEE-CALLBACK-SECRET"] = "secret-1";

        Assert.Equal(VbeeCallbackAuthorizationStatus.Authorized, SceneAudioEndpoints.GetCallbackAuthorizationStatus(context.Request, "secret-1"));

        context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?secret=secret-1");

        Assert.Equal(VbeeCallbackAuthorizationStatus.Authorized, SceneAudioEndpoints.GetCallbackAuthorizationStatus(context.Request, "secret-1"));
    }

    [Fact]
    public void CallbackConfigurationWithoutSecretFailsClearly()
    {
        var options = new VbeeOptions
        {
            CallbackUrl = "https://dashboard.example.com/api/providers/vbee/callback"
        };

        var uri = options.GetCallbackUriOrNull();

        Assert.NotNull(uri);
        Assert.Equal("https://dashboard.example.com/api/providers/vbee/callback", uri!.ToString());
    }

    [Fact]
    public void RuntimeConfig_UsesDatabaseBeforeFallbackAndEnvironment()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vbee:ApiToken"] = "config-token",
                ["Vbee:AppId"] = "config-app",
                ["TodoX:PublicBaseUrl"] = "https://dashboard.example.com"
            })
            .Build();

        var fallback = new VbeeOptions
        {
            ApiBaseUrl = "https://vbee.example/api/v1",
            TtsPath = "/tts",
            ApiToken = "fallback-token",
            AppId = "fallback-app",
            CallbackSecret = "fallback-secret",
            DefaultSampleRate = 24000,
            DefaultBitrate = 128,
            DefaultSpeedRate = 1.0m
        };

        var resolved = VbeeRuntimeConfigProvider.Resolve(
            new Dictionary<string, string?>
            {
                [VbeeRuntimeConfigProvider.TokenKey] = "db-token",
                [VbeeRuntimeConfigProvider.AppIdKey] = "db-app",
                [VbeeRuntimeConfigProvider.CallbackSecretKey] = "db-secret",
                [VbeeRuntimeConfigProvider.CallbackUrlKey] = "https://db.example.com/api/providers/vbee/callback",
                [VbeeRuntimeConfigProvider.ApiBaseKey] = "https://db.example/api/v1",
                [VbeeRuntimeConfigProvider.TtsUrlKey] = "https://db.example/api/v1/tts",
                [VbeeRuntimeConfigProvider.SampleRateKey] = "22050",
                [VbeeRuntimeConfigProvider.BitrateKey] = "192",
                [VbeeRuntimeConfigProvider.SpeedRateKey] = "0.95"
            },
            configuration,
            fallback);

        Assert.Equal("db-token", resolved.ApiToken);
        Assert.Equal("db-app", resolved.AppId);
        Assert.Equal("db-secret", resolved.CallbackSecret);
        Assert.Equal("https://db.example/api/v1", resolved.ApiBaseUrl.TrimEnd('/'));
        Assert.Equal("/tts", resolved.TtsPath);
        Assert.Equal(22050, resolved.DefaultSampleRate);
        Assert.Equal(192, resolved.DefaultBitrate);
        Assert.Equal(0.95m, resolved.DefaultSpeedRate);
        Assert.Equal("https://db.example.com/api/providers/vbee/callback?secret=db-secret", resolved.CallbackUrl);
    }

    [Fact]
    public void RuntimeConfig_LoadSqlExtractsJsonbScalarText()
    {
        Assert.Contains("config_value #>> '{}' AS config_value", VbeeRuntimeConfigProvider.LoadConfigSql, StringComparison.Ordinal);
        Assert.DoesNotContain("config_value::text", VbeeRuntimeConfigProvider.LoadConfigSql, StringComparison.OrdinalIgnoreCase);

        var configuration = new ConfigurationBuilder().Build();
        var fallback = new VbeeOptions
        {
            ApiBaseUrl = "https://vbee.example/api/v1",
            TtsPath = "/tts",
            ApiToken = "fallback-token",
            DefaultSampleRate = 24000,
            DefaultBitrate = 128,
            DefaultSpeedRate = 1.0m
        };

        var resolved = VbeeRuntimeConfigProvider.Resolve(
            new Dictionary<string, string?>
            {
                [VbeeRuntimeConfigProvider.TokenKey] = "db-token"
            },
            configuration,
            fallback);

        Assert.Equal("db-token", resolved.ApiToken);
        Assert.NotEqual("\"db-token\"", resolved.ApiToken);
    }

    [Fact]
    public void RuntimeConfig_FallsBackWhenDatabaseIsMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VBEE_API_TOKEN"] = "env-token",
                ["VBEE_APP_ID"] = "env-app",
                ["VBEE_CALLBACK_SECRET"] = "env-secret",
                ["TodoX:PublicBaseUrl"] = "https://dashboard.example.com"
            })
            .Build();

        var fallback = new VbeeOptions
        {
            ApiBaseUrl = "https://vbee.example/api/v1",
            TtsPath = "/tts",
            ApiToken = null,
            AppId = null,
            CallbackSecret = null,
            DefaultSampleRate = 24000,
            DefaultBitrate = 128,
            DefaultSpeedRate = 1.0m
        };

        var resolved = VbeeRuntimeConfigProvider.Resolve(null, configuration, fallback);

        Assert.Equal("env-token", resolved.ApiToken);
        Assert.Equal("env-app", resolved.AppId);
        Assert.Equal("env-secret", resolved.CallbackSecret);
    }

    [Fact]
    public void CallbackAuthorization_RejectsMissingOrWrongSecret()
    {
        var missing = new DefaultHttpContext();
        Assert.Equal(VbeeCallbackAuthorizationStatus.MissingSecret, SceneAudioEndpoints.GetCallbackAuthorizationStatus(missing.Request, "secret-1"));

        var wrong = new DefaultHttpContext();
        wrong.Request.QueryString = new QueryString("?secret=bad");
        Assert.Equal(VbeeCallbackAuthorizationStatus.InvalidSecret, SceneAudioEndpoints.GetCallbackAuthorizationStatus(wrong.Request, "secret-1"));
    }

    [Fact]
    public void CallbackUrl_UsesExplicitCallbackUrlAndDoesNotRequirePublicBaseUrl()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vbee:CallbackUrl"] = "https://dashboard.example.com/api/providers/vbee/callback"
            })
            .Build();

        var callbackUrl = VbeeRuntimeConfigProvider.ResolveCallbackUrl(configuration, "secret-1");

        Assert.Equal("https://dashboard.example.com/api/providers/vbee/callback?secret=secret-1", callbackUrl);
        Assert.DoesNotContain("TodoX:PublicBaseUrl", callbackUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeConfig_DoesNotLeakSecretsInDiagnosticShape()
    {
        var eventData = new
        {
            provider = "vbee",
            requestId = "req-1",
            projectId = 11,
            sceneId = 22,
            audioVersionId = Guid.NewGuid(),
            callbackConfigured = true
        };

        var json = JsonSerializer.Serialize(eventData);

        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeConfig_AllowsMissingCallbackSecretAndPublicBaseUrl()
    {
        var configuration = new ConfigurationBuilder()
            .Build();

        var fallback = new VbeeOptions
        {
            ApiBaseUrl = "https://vbee.example/api/v1",
            TtsPath = "/tts",
            ApiToken = "token",
            AppId = "app",
            CallbackSecret = null,
            DefaultSampleRate = 24000,
            DefaultBitrate = 128,
            DefaultSpeedRate = 1.0m
        };

        var resolved = VbeeRuntimeConfigProvider.Resolve(null, configuration, fallback);

        Assert.Null(resolved.CallbackUrl);
        Assert.Null(resolved.CallbackSecret);
    }

    [Fact]
    public void SceneAudioHandler_SourceNoLongerDependsOnCallbackUrlForSubmitOrPoll()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneAudioRenderHandler.cs");

        Assert.DoesNotContain("BuildAuthorizedCallbackUriOrNull", source);
        Assert.DoesNotContain("callback_url", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SampleRateRetry", source, StringComparison.Ordinal);
        Assert.Contains("GetStatusAsync(requestId", source, StringComparison.Ordinal);
        Assert.Contains("ScheduleProviderPollAsync(job.Id, options.PollInterval, \"VBEE_PENDING\"", source, StringComparison.Ordinal);
        Assert.Contains("ScheduleProviderPollAsync(job.Id, options.PollInterval, \"VBEE_SUBMITTED\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmitAsync(retryRequest", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneAudioHandler_PersistsRequestIdAndPollsExistingTaskWithoutResubmitting()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneAudioRenderHandler.cs");
        var pollingStart = source.IndexOf("SCENE_AUDIO_PROVIDER_POLLING", StringComparison.Ordinal);
        var pollingEnd = source.IndexOf("var status = await _vbee.GetStatusAsync", pollingStart, StringComparison.Ordinal);

        Assert.True(pollingStart >= 0);
        Assert.True(pollingEnd > pollingStart);
        Assert.Contains("MarkSceneAudioVersionSubmittedAsync(version.Id, \"vbee\", input.VoiceCode, null, requestId, ct)", source, StringComparison.Ordinal);
        Assert.Contains("var status = await _vbee.GetStatusAsync(requestId, options, ct);", source, StringComparison.Ordinal);
        Assert.Contains("ScheduleProviderPollAsync(job.Id, options.PollInterval, \"VBEE_PENDING\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmitAsync", source[pollingStart..pollingEnd], StringComparison.Ordinal);
    }

    private static string CreateTempContentRoot(out string audioPath)
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        audioPath = Path.GetFullPath(Path.Combine(
            contentRoot,
            "wwwroot",
            "uploads",
            "render-projects",
            "a",
            "b",
            "scene-audio.mp3"));
        Directory.CreateDirectory(Path.GetDirectoryName(audioPath)!);
        File.WriteAllText(audioPath, "mp3");
        return contentRoot;
    }

    private static LocalMediaPathResolver CreateResolver(string contentRoot)
        => new(
            new StubWebHostEnvironment(contentRoot),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:LocalUploadRoot"] = "wwwroot/uploads",
                    ["Storage:PublicUploadBase"] = "/uploads"
                })
                .Build());

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "TodoX.Web" }.Concat(parts).ToArray()));

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public StubWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            WebRootPath = Path.Combine(contentRootPath, "wwwroot");
            ContentRootFileProvider = new NullFileProvider();
            WebRootFileProvider = new NullFileProvider();
        }

        public string ApplicationName { get; set; } = "TodoX.Web.Tests";
        public IFileProvider ContentRootFileProvider { get; set; }
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Development";
        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
    }

    private sealed class StubMediaFileService : IMediaFileService
    {
        private readonly MediaFileDto? _media;

        public StubMediaFileService(MediaFileDto? media)
        {
            _media = media;
        }

        public Task<MediaFileDto?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_media?.Id == id ? _media : null);

        public Task<MediaFileDto> SaveAsync(byte[] content, string originalFileName, string mimeType, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<MediaFileDto> SaveAtObjectKeyAsync(byte[] content, string objectKey, string originalFileName, string mimeType, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<MediaFileDto?> GetByObjectKeyAsync(string objectKey, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<MediaFileDto?> GetByObjectKeyAsync(Guid tenantId, string objectKey, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<MediaFileDto?> GetByPublicUrlAsync(string publicUrl, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<byte[]?> ReadBytesAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<MediaFileDto> ReplaceContentAsync(Guid mediaId, byte[] content, string mimeType, Guid userId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> IsOwnedByAsync(Guid mediaId, Guid userId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ReferenceImage?> BuildReferenceImageAsync(Guid mediaId, string role, Guid userId, bool enforceOwnership = true, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<MediaFileDto> DownloadAndSaveImageAsync(string imageUrl, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<MediaFileDto> DownloadAndSaveImageAtObjectKeyAsync(string imageUrl, string objectKey, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<MediaFileDto> SaveBinaryAtObjectKeyAsync(byte[] content, string objectKey, string originalFileName, string mimeType, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<MediaFileDto> DownloadAndSaveBinaryAtObjectKeyAsync(string fileUrl, string objectKey, string fileCategory, string expectedMimeType, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
