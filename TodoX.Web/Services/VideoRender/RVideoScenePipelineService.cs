using TodoX.Web.Models;
using TodoX.Web.Services.Render;
using TodoX.Web.Components.Pages.RVideo;

namespace TodoX.Web.Services.VideoRender;

// RVideo V2 pipeline steps the legacy page had as buttons: build the scenes from the generated prompt
// ("Tạo / phân tách scene") and queue the missing scene images ("Tạo ảnh"). Scene videos then follow
// via the AUTO lifecycle worker or the V2 "Render job" dialog.
public interface IRVideoScenePipelineService
{
    Task<RVideoSceneLinkResult> CreateScenesFromPromptAsync(long projectId, bool linked, CurrentUserSession user, CancellationToken ct = default);
    Task<RVideoSceneLinkResult> EnqueueMissingImagesAsync(long projectId, CurrentUserSession user, CancellationToken ct = default);
}

public sealed class RVideoScenePipelineService : IRVideoScenePipelineService
{
    private readonly VideoRenderRepository _repo;
    private readonly RVideoJobSettingsRepository _settings;
    private readonly IRenderJobService _jobs;
    private readonly ILogger<RVideoScenePipelineService> _logger;

    public RVideoScenePipelineService(VideoRenderRepository repo, RVideoJobSettingsRepository settings, IRenderJobService jobs, ILogger<RVideoScenePipelineService> logger)
    {
        _repo = repo;
        _settings = settings;
        _jobs = jobs;
        _logger = logger;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VideoProjectDto, object> PromptSceneCache = new();

    /// <summary>True when the project's generated prompt lists scenes (cached per loaded project object).</summary>
    public static bool PromptHasScenes(VideoProjectDto project)
        => (bool)PromptSceneCache.GetValue(project, p =>
        {
            try { return new TodoXVideoPromptParser().Parse(p.OriginalPrompt ?? string.Empty).HasScenes; }
            catch { return false; }
        });

    // Progress counts for the V2 step guide (scene outputs, not the input reference images).
    public static int CountScenesWithImage(VideoProjectDto project) => project.Scenes.Count(x => !string.IsNullOrWhiteSpace(x.StaticImageUrl));
    public static int CountScenesWithVideo(VideoProjectDto project) => project.Scenes.Count(x => !string.IsNullOrWhiteSpace(x.SceneVideoUrl));

    /// <summary>Scenes exactly as the legacy page built them from the prompt JSON (fallback: evenly split placeholders).</summary>
    public static List<VideoProjectSceneDto> BuildScenes(string? prompt, int totalSeconds, int sceneSeconds)
    {
        var parsed = new TodoXVideoPromptParser().Parse(prompt ?? string.Empty);
        var seconds = Math.Max(1, sceneSeconds);
        if (parsed.HasScenes)
        {
            return parsed.Model.Scenes
                .OrderBy(x => x.Scene ?? int.MaxValue)
                .Select((scene, index) => new VideoProjectSceneDto
                {
                    SceneIndex = scene.Scene ?? index + 1,
                    Title = $"Scene {scene.Scene ?? index + 1}",
                    DurationSeconds = scene.DurationSeconds ?? seconds,
                    ScenePrompt = new ScenePromptMetadata
                    {
                        ScenePurpose = scene.ScenePurpose,
                        ImagePrompt = scene.EffectiveImagePrompt ?? scene.ImagePrompt,
                        EffectiveImagePrompt = scene.EffectiveImagePrompt,
                        MotionPrompt = scene.MotionPrompt,
                        Voice = scene.Voice,
                        VoiceInstruction = scene.VoiceInstruction,
                        TtsRate = scene.TtsRate,
                        RawSceneJson = scene.RawJson
                    }.Serialize(),
                    ImagePrompt = scene.EffectiveImagePrompt,
                    VideoPrompt = scene.MotionPrompt,
                    Status = VideoSceneStatuses.Draft
                })
                .ToList();
        }
        return new List<VideoProjectSceneDto>(); // no scenes in the prompt: nothing sensible to build
    }

    public async Task<RVideoSceneLinkResult> CreateScenesFromPromptAsync(long projectId, bool linked, CurrentUserSession user, CancellationToken ct = default)
    {
        try
        {
            var project = await _repo.GetProjectAsync(projectId, user, ct);
            if (project is null) return new(false, "Không tìm thấy dự án hoặc bạn không có quyền.");
            var scenes = BuildScenes(project.OriginalPrompt, project.TotalSeconds, project.SceneSeconds);
            if (scenes.Count == 0) return new(false, "Prompt chưa có danh sách scene. Hãy bấm Tạo Prompt AI trước.");
            if (linked) RVideoSceneLinkService.ApplyLinkedToNewScenes(scenes);
            await _repo.ReplaceScenesAsync(projectId, scenes, ct);
            await _repo.AddProjectEventAsync(projectId, "SCENES_CREATED", "info", "RVIDEO scenes created from the generated prompt (V2).",
                new { projectId, sceneCount = scenes.Count, linked }, ct);
            return new(true, $"Đã tạo {scenes.Count} scene.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "RVIDEO_V2_CREATE_SCENES_FAILED projectId={ProjectId}", projectId);
            return new(false, RVideoUserText.Error(ex.Message) ?? "Chưa tạo được scene. Vui lòng thử lại.");
        }
    }

    public async Task<RVideoSceneLinkResult> EnqueueMissingImagesAsync(long projectId, CurrentUserSession user, CancellationToken ct = default)
    {
        try
        {
            var project = await _repo.GetProjectAsync(projectId, user, ct);
            if (project is null) return new(false, "Không tìm thấy dự án hoặc bạn không có quyền.");
            if (project.Scenes.Count == 0) return new(false, "Chưa có scene để tạo ảnh.");
            var setting = await _settings.GetAsync(projectId, ct);
            if (setting is null) return new(false, "Chưa có cấu hình video.");
            if (setting.UseReferenceImageForAllScenes)
                return new(false, "Đang dùng ảnh tham chiếu chung cho mọi scene nên không cần tạo ảnh riêng. Hãy tạo video.");

            var reference = RVideoSceneImageReferenceSelection.Resolve(setting);
            var input = RVideoSceneImageReferenceSelection.BuildBatchInput(reference);
            input.CapabilityCode = SceneImageRenderContext.RVideoCapabilityCode;
            input.ProjectId = projectId;
            input.AspectRatio = RVideoRules.ResolveRenderSettings(project.OriginalPrompt).AspectRatio;
            input.UserId = user.UserId;
            input.CustomerId = user.CustomerId;
            input.TrustedPayerContext = AiProviders.AiBillingPayerResolver.CreateTrustedBackgroundContext(user);
            input.OnlyMissingOrFailed = true;
            input.ParentJobBilled = false;
            // Linked scenes take their image from the previous scene's last frame, not from this batch.
            var ordered = project.Scenes.OrderBy(x => x.SceneIndex).ToList();
            input.SceneIds = ordered.Where(x => !RVideoSceneLink.IsLinked(x, ordered)).Select(x => x.Id).ToArray();

            var (job, alreadyActive) = await _jobs.EnqueueForProjectIfNoneActiveAsync(new RenderJobCreateModel
            {
                JobType = SceneImageBatchRenderHandler.JobTypeName,
                UserId = user.UserId,
                CustomerId = user.CustomerId,
                Input = input,
                Prompt = new { projectId, source = "rvideo_v2" },
                LogCode = $"video-image-{projectId}",
                ProviderCode = SceneImageBatchRenderHandler.RoutingProviderCode,
                ModelCode = SceneImageBatchRenderHandler.RoutingModelCode,
                MaxAttempts = 1,
                PointCostEstimate = 0,
                PointStatus = RenderPointStatuses.NotRequired
            }, projectId, ct);
            if (alreadyActive) return new(false, "Dự án đang tạo ảnh, vui lòng đợi.");
            await _repo.AddProjectEventAsync(projectId, "SCENE_IMAGE_BATCH_ENQUEUED", "info", "Đã enqueue batch render ảnh tĩnh (V2).",
                new { job.Id, onlyMissingOrFailed = true, input.CharacterId }, ct);
            return new(true, "Đang tạo ảnh cho các scene.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "RVIDEO_V2_ENQUEUE_IMAGES_FAILED projectId={ProjectId}", projectId);
            return new(false, RVideoUserText.Error(ex.Message) ?? "Chưa tạo được ảnh. Vui lòng thử lại.");
        }
    }
}
