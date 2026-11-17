using System.Text.Json;
using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.Mcp;

// AI image/video generation through TodoX; results land as files on this computer.
public sealed partial class VideoTools
{
    private const string AiOptionParam = "Only a value listed for the model by todox_ai_models and confirmed by the user; never guess.";
    private const string AiFolderParams = ProjectParam + " The result is saved into that project's 06-ai folder.";

    [McpServerTool(Name = "todox_ai_models", ReadOnly = true, Idempotent = true)]
    [Description("List TodoX AI models for making images, videos or music: options (ratios, resolutions, modes, durations) and abilities. Prices are not listed: TodoX shows the exact price only in its confirmation window when creating. A model with available=false cannot be used yet. Pass model to see one model. Call before todox_image_create / todox_video_create / todox_music_create. VFX (effects on an existing clip) uses the video models with the edit ability (withEdit) and the clip as a source video.")]
    public async Task<string> AiModels(
        CancellationToken ct,
        [Description("image, video or music; omit for all.")] string? type = null,
        [Description("A model id to see only that model.")] string? model = null) =>
        await ReadyProblemAsync(null, ct) ?? await _ai.ModelsAsync(type, model, ct);

    [McpServerTool(Name = "todox_image_create", Destructive = false)]
    [Description("Make an AI image with TodoX (costs TodoX points; you never see or state a price: before calling, tell the user TodoX will show the exact price in its confirmation window and they must click \"Đồng ý trừ điểm\" there; if they refuse nothing is made or charged - do not retry unless they ask). Input images can be files on this computer or https links. Waits for the result and saves it on this computer; returns the local file path, or a task id to check with todox_image_status.")]
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
    [Description("Make an AI video with TodoX (costs TodoX points; you never see or state a price: before calling, tell the user TodoX will show the exact price in its confirmation window and they must click \"Đồng ý trừ điểm\" there; if they refuse nothing is made or charged - do not retry unless they ask). Start/end frames and reference images can be files on this computer or https links; source videos must be https links. Waits a few minutes; if the video is not ready yet it returns a task id to check with todox_video_status. Finished videos are saved on this computer and the local path is returned.")]
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

    [McpServerTool(Name = "todox_upload", Destructive = false)]
    [Description("Put a photo, clip or audio file from this computer into the customer's TodoX storage and get a public https link, e.g. a source video for an AI model that only takes links. Files stay 7 days and count against the package's storage (1 GB by default). todox_image_create / todox_video_create already do this for local files when a project is given, so use this only when a link itself is needed.")]
    public async Task<string> Upload(
        [Description("File on this computer.")] string path,
        [Description("Project name (the project folder), e.g. \"Nhà phố Quận 7\".")] string project,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken ct)
    {
        if (await ReadyProblemAsync(null, ct) is { } problem)
        {
            return problem;
        }

        var (file, error) = await new StorageUploader(_server).UploadAsync(path, project,
            new Progress<double>(p => Report(progress, (int)(p * 100), $"Đang tải lên TodoX {p:P0}")), ct);
        return JsonSerializer.Serialize(file is null
            ? new { ok = false, message = error, url = (string?)null, expires = (string?)null, storage = (string?)null }
            : new
            {
                ok = true,
                message = (string?)$"Đã tải {file.FileName} lên kho TodoX; link dùng được 7 ngày.",
                url = (string?)file.Url,
                expires = (string?)file.ExpiresAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                storage = (string?)$"{StorageUploader.Size(file.UsedBytes)} / {StorageUploader.Size(file.LimitBytes)}"
            }, StatusTools.Json);
    }

    [McpServerTool(Name = "todox_music_create", Destructive = false)]
    [Description("Make AI music (Suno) with TodoX for a video: background music or a song with lyrics. Paid: confirm name and style with the user first; costs TodoX points; you never see or state a price: before calling, tell the user TodoX will show the exact price in its confirmation window and they must click \"Đồng ý trừ điểm\" there; if they refuse nothing is made or charged - do not retry unless they ask. Usually returns two versions, saved into the project's 03-am-thanh folder; or a task id to check with todox_music_status.")]
    public async Task<string> AiMusicCreate(
        [Description("Song / track title, at least 4 characters.")] string name,
        [Description("Style: genre, mood, instruments, tempo (BPM), vocal tone, e.g. upbeat pop, guitar, 120 BPM, bright female vocal.")] string styles,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken ct,
        [Description("Lyrics with [Intro], [Verse], [Chorus], [Bridge], [Outro] tags; omit for instrumental background music.")] string? lyrics = null,
        [Description("Singer's voice: m or f.")] string? gender = null,
        [Description("Music model from todox_ai_models type music; omit for TodoX's default.")] string? model = null,
        [Description(ProjectParam + " The songs go into its 03-am-thanh folder.")] string? project = null)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            return NeedProject;
        }

        await using var session = await SessionLogger.StartAsync(_server, _paths, "ai_music", ct);
        if (await ReadyProblemAsync(session, ct) is { } problem)
        {
            return problem;
        }

        var result = await _ai.CreateMusicAsync(name, styles, lyrics, gender, model, string.IsNullOrWhiteSpace(project) ? null : project.Trim(),
            MusicFolder(project), TimeSpan.FromMinutes(4), message => Report(progress, 50, message), ct);
        session.Info("ai_create", null, new { kind = "music", model });
        return result;
    }

    [McpServerTool(Name = "todox_music_status", Destructive = false, Idempotent = true)]
    [Description("Check an AI music task from todox_music_create; when it is finished the songs are saved on this computer and the local paths returned.")]
    public async Task<string> AiMusicStatus(
        [Description("Task id returned by todox_music_create or todox_ai_tasks.")] string task,
        CancellationToken ct,
        [Description("Project name: the songs go into its 03-am-thanh folder.")] string? project = null) =>
        await ReadyProblemAsync(null, ct) ?? await _ai.StatusAsync(task, MusicFolder(project), ct);

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
    [Description("List this customer's recent AI image/video tasks with status.")]
    public async Task<string> AiTasks(
        CancellationToken ct,
        [Description("image, video or music; omit for all.")] string? type = null,
        [Description("How many, up to 50 (default 20).")] int limit = 20) =>
        await ReadyProblemAsync(null, ct) ?? await _ai.TasksAsync(type, limit, ct);

    private async Task<string> AiCreateAsync(AiCreateInput input, string? project, TimeSpan wait, IProgress<ProgressNotificationValue> progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            return NeedProject;
        }

        await using var session = await SessionLogger.StartAsync(_server, _paths, $"ai_{input.Kind}", ct);
        if (await ReadyProblemAsync(session, ct) is { } problem)
        {
            return problem;
        }

        var result = await _ai.CreateAsync(input with { Project = string.IsNullOrWhiteSpace(project) ? null : project.Trim() }, AiFolder(project), wait, message => Report(progress, 50, message), ct);
        session.Info("ai_create", null, new { kind = input.Kind, model = input.Model });
        return result;
    }

    private async Task<string> AiStatusAsync(string task, string? project, CancellationToken ct) =>
        await ReadyProblemAsync(null, ct) ?? await _ai.StatusAsync(task, AiFolder(project), ct);

    private string MusicFolder(string? project) =>
        string.IsNullOrWhiteSpace(project)
            ? AiGenerator.DefaultFolder()
            : _projects.Find(project, create: true).Folder?.Path is { } dir ? Path.Combine(dir, Projects.ProjectFolders.AudioDir) : AiGenerator.DefaultFolder();

    private string AiFolder(string? project) =>
        string.IsNullOrWhiteSpace(project)
            ? AiGenerator.DefaultFolder()
            : _projects.Find(project, create: true).Folder?.Path is { } dir ? Path.Combine(dir, Projects.ProjectFolders.AiDir) : AiGenerator.DefaultFolder();
}
