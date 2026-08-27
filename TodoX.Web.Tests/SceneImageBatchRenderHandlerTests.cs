using TodoX.Web.Models;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public class SceneImageBatchRenderHandlerTests
{
    [Fact]
    public void ShouldRenderScene_WhenOnlyMissingOrFailed_SkipsSuccessfulImages()
    {
        Assert.False(SceneImageBatchRenderHandler.ShouldRenderScene(new VideoProjectSceneDto
        {
            StaticImageUrl = "https://cdn/scene.png",
            Status = VideoSceneStatuses.ImageReady
        }, onlyMissingOrFailed: true));
    }

    [Fact]
    public void ShouldRenderScene_WhenOnlyMissingOrFailed_IncludesMissingAndFailed()
    {
        Assert.True(SceneImageBatchRenderHandler.ShouldRenderScene(new VideoProjectSceneDto
        {
            StaticImageUrl = null,
            Status = VideoSceneStatuses.Draft
        }, onlyMissingOrFailed: true));

        Assert.True(SceneImageBatchRenderHandler.ShouldRenderScene(new VideoProjectSceneDto
        {
            StaticImageUrl = "https://cdn/old.png",
            Status = VideoSceneStatuses.Failed
        }, onlyMissingOrFailed: true));
    }

    [Fact]
    public void SceneImageLogicalRequest_ForBatch_IsStableForSameJobAndScene()
    {
        var jobId = Guid.NewGuid();

        var first = SceneImageRenderService.BuildLogicalRequestId("render_job_scene_image", 42, jobId);
        var second = SceneImageRenderService.BuildLogicalRequestId("render_job_scene_image", 42, jobId);

        Assert.Equal(first, second);
        Assert.Contains(jobId.ToString("N"), first);
        Assert.Contains("scene-42", first);
    }

    [Fact]
    public void SceneImageLogicalRequest_ForDifferentJobs_CreatesDifferentVersionOperations()
    {
        var first = SceneImageRenderService.BuildLogicalRequestId("render_job_scene_image", 42, Guid.NewGuid());
        var second = SceneImageRenderService.BuildLogicalRequestId("render_job_scene_image", 42, Guid.NewGuid());

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void SceneImageLogicalRequest_ForDifferentScenes_CreatesDifferentVersionOperations()
    {
        var jobId = Guid.NewGuid();

        var first = SceneImageRenderService.BuildLogicalRequestId("render_job_scene_image", 41, jobId);
        var second = SceneImageRenderService.BuildLogicalRequestId("render_job_scene_image", 42, jobId);

        Assert.NotEqual(first, second);
        Assert.Contains("scene-41", first);
        Assert.Contains("scene-42", second);
    }

    [Fact]
    public void SceneImageBatchJobMetadata_IsRouterNeutral()
    {
        Assert.Equal("configured_image_router", SceneImageBatchRenderHandler.RoutingProviderCode);
        Assert.Equal("scene_image_default", SceneImageBatchRenderHandler.RoutingModelCode);
        Assert.NotEqual("todox_image", SceneImageBatchRenderHandler.RoutingProviderCode);
        Assert.NotEqual("vertex_scene_image", SceneImageBatchRenderHandler.RoutingModelCode);
    }

    [Fact]
    public void SceneImageBatchInput_DefaultsToRVideoCapability()
    {
        Assert.Equal(SceneImageRenderContext.RVideoCapabilityCode, new SceneImageBatchInput().CapabilityCode);
        Assert.Equal("NONE", new SceneImageBatchInput().ReferenceSource);
        Assert.Equal("rvideo_scene_image_generation", SceneImageRenderContext.RVideoCapabilityCode);
    }

    [Fact]
    public void RVideoSceneReference_NoneDoesNotRequestOrCarryCharacterMedia()
    {
        var reference = RVideoSceneImageReferenceSelection.Resolve(
            skipCharacter: true,
            characterMode: RVideoCharacterModes.None,
            uploadObjectKey: "upload/key",
            uploadUrl: "https://cdn/upload.png",
            libraryCharacterId: 42,
            libraryObjectKey: "library/key",
            libraryUrl: "https://cdn/library.png",
            libraryCharacterPrompt: "library prompt");

        Assert.False(reference.ReferenceRequested);
        Assert.Null(reference.CharacterId);
        Assert.Null(reference.ObjectKey);
        Assert.Null(reference.Url);
        Assert.Equal("NONE", reference.Source);
    }

    [Fact]
    public void RVideoSceneReference_UploadUsesOnlyUploadedSnapshot()
    {
        var reference = RVideoSceneImageReferenceSelection.Resolve(
            skipCharacter: false,
            characterMode: RVideoCharacterModes.Upload,
            uploadObjectKey: "upload/key",
            uploadUrl: "https://cdn/upload.png",
            libraryCharacterId: 42,
            libraryObjectKey: "library/key",
            libraryUrl: "https://cdn/library.png",
            libraryCharacterPrompt: "library prompt");

        Assert.True(reference.ReferenceRequested);
        Assert.Null(reference.CharacterId);
        Assert.Equal("upload/key", reference.ObjectKey);
        Assert.Equal("https://cdn/upload.png", reference.Url);
        Assert.Equal("UPLOAD", reference.Source);
    }

    [Fact]
    public void RVideoSceneReference_LibraryUsesSelectedCharacterMedia()
    {
        var reference = RVideoSceneImageReferenceSelection.Resolve(
            skipCharacter: false,
            characterMode: RVideoCharacterModes.Library,
            uploadObjectKey: "upload/key",
            uploadUrl: "https://cdn/upload.png",
            libraryCharacterId: 42,
            libraryObjectKey: "library/key",
            libraryUrl: "https://cdn/library.png",
            libraryCharacterPrompt: "library prompt");

        Assert.True(reference.ReferenceRequested);
        Assert.Equal(42, reference.CharacterId);
        Assert.Equal("library/key", reference.ObjectKey);
        Assert.Equal("https://cdn/library.png", reference.Url);
        Assert.Equal("library prompt", reference.CharacterPrompt);
        Assert.Equal("LIBRARY", reference.Source);
    }

    [Fact]
    public void RVideoImageModelPolicy_UsesOrdered79AiOnlyFallbacks()
    {
        var initial = RVideoImageModelPolicy.GetInitial();
        var fallback1 = RVideoImageModelPolicy.GetNext(initial.AttemptIndex);
        var fallback2 = RVideoImageModelPolicy.GetNext(fallback1!.AttemptIndex);

        Assert.Equal("google_image_gen_banana_2", initial.Model);
        Assert.Equal("vip", initial.Mode);
        Assert.Equal("1k", initial.Resolution);
        Assert.Equal("imagegen_2_0", fallback1.Model);
        Assert.Equal("low_basic", fallback1.Mode);
        Assert.Equal("1k", fallback1.Resolution);
        Assert.Equal("seedream_4_5", fallback2!.Model);
        Assert.Equal("vip", fallback2.Mode);
        Assert.Equal("2k", fallback2.Resolution);
        Assert.Null(RVideoImageModelPolicy.GetNext(fallback2.AttemptIndex));
    }

    [Fact]
    public void ProjectJobLockName_IsStableForSameProjectAndJobType()
    {
        var first = RenderJobService.BuildProjectJobLockName("render_scene_images", 123);
        var second = RenderJobService.BuildProjectJobLockName(" render_scene_images ", 123);

        Assert.Equal(first, second);
        Assert.Contains("render_scene_images", first);
        Assert.Contains("123", first);
    }

    [Fact]
    public void SceneImageLogicalRequest_ForUserRerender_IsNewOperation()
    {
        var first = SceneImageRenderService.BuildLogicalRequestId("render_job_scene_image_rerender", 42, null);
        var second = SceneImageRenderService.BuildLogicalRequestId("render_job_scene_image_rerender", 42, null);

        Assert.NotEqual(first, second);
        Assert.StartsWith("render_job_scene_image_rerender-scene-42-", first);
    }

    [Fact]
    public void SceneMediaStorageKeys_UseImmutableVersionFolders()
    {
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var imageVersionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var videoVersionId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var finalVersionId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        Assert.Equal(
            "render-projects/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/12/scenes/34/images/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/output/scene-image.png",
            SceneMediaStorageKeys.SceneImageOutput(tenantId, 12, 34, imageVersionId, "png"));
        Assert.Equal(
            "rvideo/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/project-12/scene-34/video/cccccccccccccccccccccccccccccccc.mp4",
            SceneMediaStorageKeys.SceneVideoOutput(tenantId, 12, 34, videoVersionId));
        Assert.Equal(
            "render-projects/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/12/final-videos/dddddddddddddddddddddddddddddddd/output/final-video.mp4",
            SceneMediaStorageKeys.FinalVideoOutput(tenantId, 12, finalVersionId));
    }

    [Fact]
    public void SceneVideoStorageKeys_AreTenantScopedAndVersionIdempotent()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var versionA = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var versionB = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var first = SceneMediaStorageKeys.SceneVideoOutput(tenantA, 12, 34, versionA);

        Assert.Equal(first, SceneMediaStorageKeys.SceneVideoOutput(tenantA, 12, 34, versionA));
        Assert.NotEqual(first, SceneMediaStorageKeys.SceneVideoOutput(tenantB, 12, 34, versionA));
        Assert.NotEqual(first, SceneMediaStorageKeys.SceneVideoOutput(tenantA, 12, 34, versionB));
    }
}
