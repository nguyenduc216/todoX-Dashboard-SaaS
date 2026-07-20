using Microsoft.Extensions.Logging.Abstractions;
using TodoX.Web.Services.AiCharacters;
using TodoX.Web.Services.Profile;
using Xunit;

namespace TodoX.Web.Tests;

public class TodoXImageProviderServiceTests
{
    [Fact]
    public async Task GenerateImage_WhenCreativeRenderReturnsMediaId_PrefersStoredMediaAndPreservesMediaId()
    {
        var mediaId = Guid.NewGuid();
        var media = new FakeMediaService();
        media.MediaById[mediaId] = new()
        {
            Id = mediaId,
            ObjectKey = "video_scene_image/todox.png",
            PublicUrl = "/uploads/video_scene_image/todox.png",
            FileUrl = "/uploads/video_scene_image/todox.png",
            MimeType = "image/png"
        };
        var creative = new CapturingCreativeRender(mediaId);
        var provider = new TodoXImageProviderService(creative, media, NullLogger<TodoXImageProviderService>.Instance);

        var response = await provider.GenerateImageAsync(new OpenRouterImageRequest
        {
            UserId = Guid.NewGuid(),
            Model = "internal_default",
            Prompt = "scene prompt",
            FileCategory = "video_scene_image",
            ReferenceMediaIds = new[] { Guid.NewGuid() },
            ReferenceImageUrls = new[] { "/uploads/characters/reference.png" }
        });

        Assert.True(response.Success);
        Assert.Equal(mediaId, response.ResultMediaId);
        Assert.Equal("/uploads/video_scene_image/todox.png", response.ImageUrl);
        Assert.Equal("video_scene_image/todox.png", response.ObjectKey);
        Assert.NotNull(creative.LastRequest);
        Assert.Equal("video_scene_image", creative.LastRequest!.FileCategory);
        Assert.Equal(response.ResultMediaId, mediaId);
        Assert.True(creative.LastRequest.RequireReferenceImages);
        Assert.Equal(response.ResultMediaId, mediaId);
        Assert.Equal(response.ObjectKey, media.MediaById[mediaId].ObjectKey);
    }

    private sealed class CapturingCreativeRender : IImageAICreativeRenderService
    {
        private readonly Guid _mediaId;
        public ImageAICreativeRenderRequest? LastRequest { get; private set; }

        public CapturingCreativeRender(Guid mediaId) => _mediaId = mediaId;

        public Task<ImageAICreativeRenderResult> RenderAsync(ImageAICreativeRenderRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new ImageAICreativeRenderResult
            {
                Status = "success",
                RenderEngineMode = "internal_default",
                Images = new List<ImageAICreativeRenderImage>
                {
                    new()
                    {
                        Status = "completed",
                        MediaId = _mediaId,
                        Url = "/uploads/old-url-should-not-win.png"
                    }
                }
            });
        }
    }
}
