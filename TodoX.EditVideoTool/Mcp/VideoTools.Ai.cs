using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.Mcp;

// AI image/video generation through TodoX; results land as files on this computer.
public sealed partial class VideoTools
{
    private const string AiOptionParam = "Only a value listed for the model by todox_ai_models and confirmed by the user; never guess.";
    private const string AiFolderParams = "Where the result is saved: a project name (its TodoX resource folder) or omit for Documents\\TodoX\\ai.";

    [McpServerTool(Name = "todox_ai_models", ReadOnly = true, Idempotent = true)]
    [Description("List TodoX AI models for making images or videos: options (ratios, resolutions, modes, durations), abilities and the point price, plus the customer's point balance. Pass model to see that model's full price table. Call before todox_image_create / todox_video_create.")]
    public async Task<string> AiModels(
        CancellationToken ct,
        [Description("image or video; omit for both.")] string? type = null,
        [Description("A model id to see its full price table.")] string? model = null) =>
        await ReadyProblemAsync(null, ct) ?? await _ai.ModelsAsync(type, model, ct);

    [McpServerTool(Name = "todox_image_create", Destructive = false)]
    [Description("Make an AI image with TodoX (costs points; confirm model, options and price with the user first). Input images can be files on this computer or https links. Waits for the result and saves it on this computer; returns the local file path, or a task id to check with todox_image_status.")]
    public Task<string> AiImageCreate(
        [Description("Model id from todox_ai_models.")] string model,
        [Description("What the image should show.")] string prompt,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken ct,
        [Description("Aspect ratio, e.g. 9:16. " + AiOptionParam)] string? ratio = null,
        [Description("Resolution. " + AiOptionParam)] string? resolution = null,
        [Description("Mode. " + AiOptionParam)] string? mode = null,
        [Description("Images to edit / subjects to keep (local files or https links).")] string[]? images = null,
        [Description("Reference images for style or elements (local files or https links).")] string[]? references = null,
        [Description(AiFolderParams)] string? project = null) =>
        AiCreateAsync(new AiCreateInput("image", model, prompt, ratio, resolution, mode, null, images, references, null),
            project, TimeSpan.FromMinutes(4), progress, ct);

    [McpServerTool(Name = "todox_video_create", Destructive = false)]
    [Description("Make an AI video with TodoX (costs points; confirm model, options and price with the user first). Start/end frames and reference images can be files on this computer or https links; source videos must be https links. Waits a few minutes; if the video is not ready yet it returns a task id to check with todox_video_status. Finished videos are saved on this computer and the local path is returned.")]
    public Task<string> AiVideoCreate(
        [Description("Model id from todox_ai_models.")] string model,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken ct,
        [Description("Scene / motion description.")] string? prompt = null,
        [Description("Aspect ratio. " + AiOptionParam)] string? ratio = null,
        [Description("Resolution. " + AiOptionParam)] string? resolution = null,
        [Description("Mode. " + AiOptionParam)] string? mode = null,
        [Description("Length in seconds as listed in the model's durations. " + AiOptionParam)] string? duration = null,
        [Description("Start frame, then optional end frame (local files or https links).")] string[]? images = null,
        [Description("Reference images (\"thành phần\"), not start frames (local files or https links).")] string[]? references = null,
        [Description("Source / reference videos as https links (for edit, motion, extend models).")] string[]? videos = null,
        [Description(AiFolderParams)] string? project = null) =>
        AiCreateAsync(new AiCreateInput("video", model, prompt, ratio, resolution, mode, duration, images, references, videos),
            project, TimeSpan.FromMinutes(4), progress, ct);

    [McpServerTool(Name = "todox_image_status", Destructive = false, Idempotent = true)]
    [Description("Check an AI image task from todox_image_create; when it is finished the image is saved on this computer and the local path returned.")]
    public Task<string> AiImageStatus(
        [Description("Task id returned by todox_image_create or todox_ai_tasks.")] string task,
        CancellationToken ct,
        [Description(AiFolderParams)] string? project = null) =>
        AiStatusAsync(task, project, ct);

    [McpServerTool(Name = "todox_video_status", Destructive = false, Idempotent = true)]
    [Description("Check an AI video task from todox_video_create; when it is finished the video is saved on this computer and the local path returned.")]
    public Task<string> AiVideoStatus(
        [Description("Task id returned by todox_video_create or todox_ai_tasks.")] string task,
        CancellationToken ct,
        [Description(AiFolderParams)] string? project = null) =>
        AiStatusAsync(task, project, ct);

    [McpServerTool(Name = "todox_ai_tasks", ReadOnly = true, Idempotent = true)]
    [Description("List this customer's recent AI image/video tasks with status and points.")]
    public async Task<string> AiTasks(
        CancellationToken ct,
        [Description("image or video; omit for both.")] string? type = null,
        [Description("How many, up to 50 (default 20).")] int limit = 20) =>
        await ReadyProblemAsync(null, ct) ?? await _ai.TasksAsync(type, limit, ct);

    private async Task<string> AiCreateAsync(AiCreateInput input, string? project, TimeSpan wait, IProgress<ProgressNotificationValue> progress, CancellationToken ct)
    {
        await using var session = await SessionLogger.StartAsync(_server, _paths, $"ai_{input.Kind}", ct);
        if (await ReadyProblemAsync(session, ct) is { } problem)
        {
            return problem;
        }

        var result = await _ai.CreateAsync(input, AiFolder(project), wait, message => Report(progress, 50, message), ct);
        session.Info("ai_create", null, new { kind = input.Kind, model = input.Model });
        return result;
    }

    private async Task<string> AiStatusAsync(string task, string? project, CancellationToken ct) =>
        await ReadyProblemAsync(null, ct) ?? await _ai.StatusAsync(task, AiFolder(project), ct);

    private string AiFolder(string? project) =>
        string.IsNullOrWhiteSpace(project)
            ? AiGenerator.DefaultFolder()
            : _projects.Find(project, create: true).Folder?.Path ?? AiGenerator.DefaultFolder();
}
