using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TodoX.Web.Models;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.ImageRender;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

// ============================================================
// Bug B — final merge cross-instance input recovery
// ============================================================
public sealed class VideoMergeCrossInstanceInputRecoveryTests : IDisposable
{
    private readonly string _root;
    private readonly string _uploadsRoot;
    private readonly string _machineAPath;

    public VideoMergeCrossInstanceInputRecoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "todox-merge-recovery-" + Guid.NewGuid().ToString("N"));
        _uploadsRoot = Path.Combine(_root, "wwwroot", "uploads");
        // "Machine A" persisted this file on its own instance-local path.
        _machineAPath = Path.Combine(_root, "machine-a", "scene-video.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(_machineAPath)!);
        File.WriteAllText(_machineAPath, "machine-a-video-bytes");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private VideoRenderMergeHandler CreateHandler(RecoveryMediaService media)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:LocalUploadRoot"] = "wwwroot/uploads",
                ["TodoX:TenantId"] = Guid.NewGuid().ToString()
            })
            .Build();
        var env = new StubWebHostEnvironment { ContentRootPath = _root };
        var optionsMonitor = new StubOptionsMonitor(new VideoRenderOptions());
        var tenant = new TodoX.Web.Services.TenantContext(null!, configuration);
        return new VideoRenderMergeHandler(
            NullLogger<VideoRenderMergeHandler>.Instance,
            optionsMonitor,
            null!,
            env,
            null!,
            null!,
            null!,
            configuration,
            media,
            tenant);
    }

    private SceneVideoVersionDto CreateVersion(string? sourceFilePath, Guid? resultMediaId, string? storageKey, string? publicUrl)
        => new()
        {
            Id = Guid.NewGuid(),
            SourceFilePath = sourceFilePath,
            ResultMediaId = resultMediaId,
            StorageKey = storageKey,
            PublicUrl = publicUrl,
            Status = "completed"
        };

    private static MediaFileDto CreateMedia(Guid id, string objectKey, string publicUrl)
        => new()
        {
            Id = id,
            ObjectKey = objectKey,
            PublicUrl = publicUrl,
            MimeType = "video/mp4",
            FileCategory = "video_scene_video",
            IsActive = true
        };

    private string LocalizedPath(string objectKey)
    {
        var path = Path.Combine(_uploadsRoot, objectKey.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "existing-scene-video-bytes");
        return path;
    }

    [Fact]
    public async Task H_ExistingLocalSourceFilePathIsUsedDirectly()
    {
        var media = new RecoveryMediaService();
        var handler = CreateHandler(media);
        var version = CreateVersion(_machineAPath, null, null, null);

        var path = await handler.ResolveLocalMergeInputAsync(version, 724, CancellationToken.None);

        Assert.Equal(_machineAPath, path);
        Assert.Equal(0, media.DownloadCalls.Count);
    }

    [Fact]
    public async Task I_MissingLocalPathWithResultMediaIdIsMaterializedLocally()
    {
        var mediaId = Guid.NewGuid();
        const string objectKey = "rvideo/scene-video.mp4";
        const string publicUrl = "https://media.example.test/rvideo/scene-video.mp4";
        var localizedPath = Path.Combine(_uploadsRoot, objectKey.Replace('/', Path.DirectorySeparatorChar));
        var media = new RecoveryMediaService();
        media.MediaById[mediaId] = CreateMedia(mediaId, objectKey, publicUrl);
        // Machine A's physical path does not exist on this (machine B) instance, and no local copy of
        // the media exists yet — the durable media must be materialized from its PublicUrl.
        media.DownloadHandler = _ =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localizedPath)!);
            File.WriteAllText(localizedPath, "downloaded-bytes");
            return localizedPath;
        };
        var handler = CreateHandler(media);
        var version = CreateVersion(_machineAPath + ".ghost", mediaId, "unrelated/other-key.mp4", publicUrl);

        var path = await handler.ResolveLocalMergeInputAsync(version, 724, CancellationToken.None);

        Assert.True(File.Exists(path));
        Assert.Contains("wwwroot" + Path.DirectorySeparatorChar + "uploads", path);
        Assert.Single(media.DownloadCalls);
        Assert.Equal(publicUrl, media.DownloadCalls[0].Url);
        Assert.Equal(objectKey, media.DownloadCalls[0].ObjectKey);
    }

    [Fact]
    public async Task J_MissingLocalPathWithStorageKeyOnlyIsMaterializedLocally()
    {
        const string objectKey = "rvideo/scene-by-key.mp4";
        const string publicUrl = "https://media.example.test/rvideo/scene-by-key.mp4";
        var localizedPath = Path.Combine(_uploadsRoot, objectKey.Replace('/', Path.DirectorySeparatorChar));
        var media = new RecoveryMediaService();
        media.MediaByObjectKey[objectKey] = CreateMedia(Guid.NewGuid(), objectKey, publicUrl);
        // The file does NOT exist locally before the download; materialization creates it.
        media.DownloadHandler = _ =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localizedPath)!);
            File.WriteAllText(localizedPath, "downloaded-bytes");
            return localizedPath;
        };
        var handler = CreateHandler(media);
        var version = CreateVersion(null, null, objectKey, publicUrl);

        var path = await handler.ResolveLocalMergeInputAsync(version, 724, CancellationToken.None);

        Assert.Equal(localizedPath, path);
        Assert.True(File.Exists(path));
        Assert.Single(media.DownloadCalls);
        Assert.Equal(objectKey, media.DownloadCalls[0].ObjectKey);
    }

    [Fact]
    public async Task L_RecoveryFailsClosedWhenDownloadUnavailable_NoProviderFallback()
    {
        var mediaId = Guid.NewGuid();
        var media = new RecoveryMediaService();
        media.MediaById[mediaId] = CreateMedia(mediaId, "rvideo/scene.mp4", "https://media.example.test/rvideo/scene.mp4");
        var handler = CreateHandler(media);
        var version = CreateVersion(null, mediaId, "rvideo/scene.mp4", "https://media.example.test/rvideo/scene.mp4");

        // Download throws (no handler): the resolver must fail closed with the precise unavailable
        // error and never fall back to any provider submit.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.ResolveLocalMergeInputAsync(version, 724, CancellationToken.None));

        Assert.Contains("RVIDEO_MERGE_INPUT_UNAVAILABLE", ex.Message);
        Assert.Single(media.DownloadCalls);
    }

    [Fact]
    public async Task M_MissingLocalFileAndMissingDurableMediaFailsWithPreciseError()
    {
        var media = new RecoveryMediaService();
        var handler = CreateHandler(media);
        var version = CreateVersion(_machineAPath + ".ghost", null, null, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.ResolveLocalMergeInputAsync(version, 724, CancellationToken.None));

        Assert.Contains("RVIDEO_MERGE_INPUT_UNAVAILABLE", ex.Message);
        Assert.Contains($"sceneVideoVersionId={version.Id}", ex.Message);
        Assert.Contains($"sourceFilePath={_machineAPath}.ghost", ex.Message);
    }

    [Fact]
    public async Task N_MultiInstanceRegression_MachineAPathMissingOnMachineB_RecoversFromDurableMedia()
    {
        // Scene video persisted on logical instance A (absolute path local to A only).
        var machineAPath = "D:\\machine-a-only\\rvideo\\scene-video.mp4";
        var mediaId = Guid.NewGuid();
        const string objectKey = "rvideo/tenant/scene-724.mp4";
        const string publicUrl = "https://media.example.test/uploads/rvideo/tenant/scene-724.mp4";
        var localizedPath = Path.Combine(_uploadsRoot, objectKey.Replace('/', Path.DirectorySeparatorChar));
        var media = new RecoveryMediaService();
        media.MediaById[mediaId] = CreateMedia(mediaId, objectKey, publicUrl);
        // No local copy exists on machine B before the download; materialization creates it.
        media.DownloadHandler = _ =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localizedPath)!);
            File.WriteAllText(localizedPath, "same-existing-video-bytes");
            return localizedPath;
        };
        var handler = CreateHandler(media);
        var version = CreateVersion(machineAPath, mediaId, "unrelated/other-key.mp4", publicUrl);

        var path = await handler.ResolveLocalMergeInputAsync(version, 724, CancellationToken.None);

        Assert.True(File.Exists(path));
        Assert.StartsWith(_uploadsRoot, path);
        Assert.Single(media.DownloadCalls);
        // Same existing media is reused (download of the already-persisted media, no provider submit).
        Assert.Equal(publicUrl, media.DownloadCalls[0].Url);
        Assert.Equal(objectKey, media.DownloadCalls[0].ObjectKey);
    }

    [Fact]
    public void LocalMediaResolver_RelativeKeyMapsToUploadRoot()
    {
        const string objectKey = "rvideo/scene.mp4";
        var localizedPath = LocalizedPath(objectKey);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:LocalUploadRoot"] = "wwwroot/uploads" })
            .Build();

        var ok = VideoRenderMergeHandler.LocalMediaResolver.TryResolveExistingLocalFile(objectKey, _root, configuration, out var path);

        Assert.True(ok);
        Assert.Equal(localizedPath, path);
    }

    [Fact]
    public void LocalMediaResolver_MissingAbsoluteFileFails()
    {
        var configuration = new ConfigurationBuilder().Build();

        var ok = VideoRenderMergeHandler.LocalMediaResolver.TryResolveExistingLocalFile("D:\\does-not-exist\\scene.mp4", _root, configuration, out _);

        Assert.False(ok);
    }

    private sealed class StubWebHostEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ContentRootPath { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = "tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string EnvironmentName { get; set; } = "Development";
        public string WebRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    }

    private sealed class StubOptionsMonitor(VideoRenderOptions current) : Microsoft.Extensions.Options.IOptionsMonitor<VideoRenderOptions>
    {
        public VideoRenderOptions CurrentValue => current;
        public IDisposable OnChange(Action<VideoRenderOptions, string?> listener) => NullDisposable.Instance;
        public VideoRenderOptions Get(string? name) => current;
        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class RecoveryMediaService : IMediaFileService
    {
        public Dictionary<Guid, MediaFileDto> MediaById { get; } = new();
        public Dictionary<string, MediaFileDto> MediaByObjectKey { get; } = new();
        public List<(string Url, string ObjectKey)> DownloadCalls { get; } = new();
        public Func<string, string>? DownloadHandler { get; set; }

        public Task<MediaFileDto?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(MediaById.GetValueOrDefault(id));

        public Task<MediaFileDto?> GetByObjectKeyAsync(string objectKey, CancellationToken ct = default)
            => Task.FromResult(MediaByObjectKey.GetValueOrDefault(objectKey));

        public Task<MediaFileDto> DownloadAndSaveBinaryAtObjectKeyAsync(string fileUrl, string objectKey, string fileCategory, string expectedMimeType, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
        {
            DownloadCalls.Add((fileUrl, objectKey));
            if (DownloadHandler is null)
            {
                throw new InvalidOperationException("TEST_NO_DOWNLOAD_HANDLER");
            }

            // The handler materializes the bytes at the local upload root (simulating the real download+save).
            DownloadHandler(fileUrl);

            return Task.FromResult(new MediaFileDto
            {
                Id = Guid.NewGuid(),
                ObjectKey = objectKey,
                PublicUrl = "/uploads/" + objectKey,
                MimeType = "video/mp4",
                FileCategory = fileCategory,
                IsActive = true
            });
        }

        public Task<MediaFileDto> SaveAsync(byte[] content, string originalFileName, string mimeType, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MediaFileDto> SaveAtObjectKeyAsync(byte[] content, string objectKey, string originalFileName, string mimeType, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MediaFileDto?> GetByObjectKeyAsync(Guid tenantId, string objectKey, CancellationToken ct = default) => Task.FromResult(MediaByObjectKey.GetValueOrDefault(objectKey));
        public Task<MediaFileDto?> GetByPublicUrlAsync(string publicUrl, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<byte[]?> ReadBytesAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream?> OpenReadAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MediaFileDto> ReplaceContentAsync(Guid mediaId, byte[] content, string mimeType, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> IsOwnedByAsync(Guid mediaId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ReferenceImage?> BuildReferenceImageAsync(Guid mediaId, string role, Guid userId, bool enforceOwnership = true, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MediaFileDto> DownloadAndSaveImageAsync(string imageUrl, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MediaFileDto> DownloadAndSaveImageAtObjectKeyAsync(string imageUrl, string objectKey, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MediaFileDto> SaveBinaryAtObjectKeyAsync(byte[] content, string objectKey, string originalFileName, string mimeType, string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
