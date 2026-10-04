using Microsoft.Extensions.Options;
using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

public interface IRVideoSceneAddDefaultResolver
{
    VideoProjectAddSceneRequest BuildAddRequest(VideoProjectDto project, int nextIndex);
}

public sealed class RVideoSceneAddDefaultResolver : IRVideoSceneAddDefaultResolver
{
    private readonly IOptionsMonitor<VideoRenderOptions> _options;
    public RVideoSceneAddDefaultResolver(IOptionsMonitor<VideoRenderOptions> options) => _options = options;

    public VideoProjectAddSceneRequest BuildAddRequest(VideoProjectDto project, int nextIndex)
    {
        var fallback = _options.CurrentValue.SceneSecondsDefault;
        if (fallback <= 0) fallback = 8;
        var duration = project.SceneSeconds > 0 ? project.SceneSeconds : fallback;
        if (duration <= 0) duration = fallback;
        return new VideoProjectAddSceneRequest
        {
            Title = $"Scene {nextIndex:00}",
            Purpose = "manual_scene",
            DurationSeconds = duration,
            ImagePrompt = $"Scene {nextIndex} image prompt",
            VideoPrompt = $"Scene {nextIndex} motion prompt"
        };
    }
}
