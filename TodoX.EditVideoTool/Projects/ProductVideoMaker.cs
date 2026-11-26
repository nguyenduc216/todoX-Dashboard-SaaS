using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using TodoX.EditVideoTool.Mcp;
using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.Projects;

/// <summary>One scene of the agent's script; the customer may edit every field before making the video.</summary>
public sealed class ProductScene
{
    public int Seconds { get; set; }
    public string Voice { get; set; } = "";
    public string ImagePrompt { get; set; } = "";
    public string MotionPrompt { get; set; } = "";
    public string? Purpose { get; set; }
}

/// <summary>An AI model as TodoX lists it: the options the customer picks from.</summary>
public sealed record AiModelChoice(string Model, string Name, IReadOnlyList<string> Ratios, IReadOnlyList<string> Resolutions,
    IReadOnlyList<string> Modes, IReadOnlyList<string> Durations, IReadOnlyList<string> Abilities)
{
    public override string ToString() => Name;
}

public sealed record ProductVideoOptions(
    string Project, string ProductImage, string Aspect,
    AiModelChoice ImageModel, string? ImageResolution,
    AiModelChoice VideoModel, string? VideoResolution, string? VideoMode,
    string? Voice, string? Outro);

/// <summary>
/// Product photo + the 79AI agent's script → a finished video, without Claude: an AI image per scene (the product
/// photo as the subject), an AI video from each image, then TodoX's own edit with narration, captions and a closing card.
/// </summary>
public sealed class ProductVideoMaker
{
    /// <summary>Recorded with the video (its info file and the dashboard job) as the way it was made.</summary>
    public const string SkillSlug = "todox-video-ai-quang-cao";
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(6);
    private readonly VideoTools _tools;
    private readonly ProjectFolders _projects;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public ProductVideoMaker(VideoTools tools, ProjectFolders projects, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _tools = tools;
        _projects = projects;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>The brief the agent reads: everything the customer typed, labelled.</summary>
    public static string Brief(string product, string? price, string? highlights, string? contact, string? extra)
    {
        var b = new StringBuilder();
        b.AppendLine("Video quảng cáo sản phẩm từ ảnh thật của sản phẩm (ảnh sẽ được giữ nguyên trong mọi cảnh).");
        b.AppendLine($"Sản phẩm: {product.Trim()}");
        Line(b, "Giá / ưu đãi", price);
        Line(b, "Điểm nổi bật", highlights);
        Line(b, "Liên hệ / kêu gọi mua", contact);
        Line(b, "Yêu cầu thêm", extra);
        b.Append("Lời đọc tiếng Việt, ngắn gọn, có câu mở đầu thu hút và câu kêu gọi mua ở cảnh cuối.");
        return b.ToString();
    }

    private static void Line(StringBuilder b, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            b.AppendLine($"{label}: {value.Trim()}");
        }
    }

    /// <summary>Scenes from the server's agent answer.</summary>
    public static List<ProductScene> Scenes(JsonElement script) =>
        script.TryGetProperty("scenes", out var scenes) && scenes.ValueKind == JsonValueKind.Array
            ? scenes.EnumerateArray().Select(s => new ProductScene
            {
                Seconds = s.TryGetProperty("seconds", out var sec) && sec.TryGetInt32(out var n) ? n : 5,
                Voice = Str(s, "voice") ?? "",
                ImagePrompt = Str(s, "imagePrompt") ?? "",
                MotionPrompt = Str(s, "motionPrompt") ?? "",
                Purpose = Str(s, "purpose")
            }).ToList()
            : [];

    /// <summary>Models of one kind from the models list, the ones TodoX can make now.</summary>
    public static List<AiModelChoice> Models(JsonElement list, string kind) =>
        list.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array
            ? models.EnumerateArray()
                .Where(m => Str(m, "kind") == kind && !(m.TryGetProperty("available", out var a) && a.ValueKind == JsonValueKind.False))
                .Select(m => new AiModelChoice(Str(m, "model") ?? "", Str(m, "name") ?? Str(m, "model") ?? "",
                    Strs(m, "ratios"), Strs(m, "resolutions"), Strs(m, "modes"), Strs(m, "durations"), Strs(m, "abilities")))
                .Where(m => m.Model.Length > 0)
                .ToList()
            : [];

    /// <summary>The model's shortest listed length that holds the scene (else its longest); null when it lists none.</summary>
    public static string? PickDuration(IReadOnlyList<string> durations, int seconds)
    {
        var listed = durations.Select(d => (Text: d, Seconds: SecondsOf(d))).Where(d => d.Seconds > 0).OrderBy(d => d.Seconds).ToList();
        if (listed.Count == 0)
        {
            return null;
        }

        return (listed.FirstOrDefault(d => d.Seconds >= seconds) is { Text: not null } fit ? fit : listed[^1]).Text;
    }

    private static readonly System.Text.RegularExpressions.Regex SpeechPart = new(
        @"SPEECH:.*?(?=\b(?:TIMELINE|END_STATE|PRESERVE|VFX|INTERACTION|PERFORMANCE|SHOT):|$)",
        System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The agent writes the spoken line into the motion prompt (for models that talk); here TodoX reads the narration
    /// itself, so the video is made without speech and nobody lip-syncs to words that are not heard.
    /// </summary>
    public static string SilentMotion(string motion) =>
        SpeechPart.Replace(motion.Trim(), "").Trim() + " No speech, no dialogue and no lip movement; the narration is added later.";

    /// <summary>The value when the model lists it; a model with a single choice gets that one; otherwise none.</summary>
    public static string? Listed(IReadOnlyList<string> choices, string? wanted) =>
        wanted is not null && choices.Contains(wanted, StringComparer.OrdinalIgnoreCase) ? wanted : choices.Count == 1 ? choices[0] : null;

    /// <summary>How the product photo goes to an image model: as the subject to keep when it can, else as a reference.</summary>
    public static bool PhotoAsReference(AiModelChoice model) =>
        !model.Abilities.Any(a => a is "withEdit" or "withSubject" or "startImage") && model.Abilities.Contains("withReference");

    public async Task<string> MakeAsync(IReadOnlyList<ProductScene> scenes, ProductVideoOptions o, Action<string> log, CancellationToken ct)
    {
        if (scenes.Count == 0)
        {
            throw new InvalidOperationException("Chưa có cảnh nào. Bấm \"Viết kịch bản\" trước.");
        }

        // 1. An image per scene with the product photo, all sent first, then waited for together.
        log($"Gửi {scenes.Count} yêu cầu tạo ảnh cảnh…");
        var photo = new[] { o.ProductImage };
        var imageTasks = new List<string>();
        for (var i = 0; i < scenes.Count; i++)
        {
            var asReference = PhotoAsReference(o.ImageModel);
            var prompt = scenes[i].ImagePrompt.Trim() + " The product must look exactly like the product in the provided photo (same shape, colours, label and logo).";
            imageTasks.Add(await SubmitAsync(new AiCreateInput("image", o.ImageModel.Model, prompt, Listed(o.ImageModel.Ratios, o.Aspect),
                o.ImageResolution, null, null, asReference ? null : photo, asReference ? photo : null, null), o.Project, $"ảnh cảnh {i + 1}", ct));
        }

        var images = await WaitAllAsync(imageTasks, o.Project, "ảnh", log, TimeSpan.FromMinutes(10), ct);

        // 2. A video from each image.
        log($"Gửi {scenes.Count} yêu cầu tạo video cảnh…");
        var videoTasks = new List<string>();
        for (var i = 0; i < scenes.Count; i++)
        {
            videoTasks.Add(await SubmitAsync(new AiCreateInput("video", o.VideoModel.Model, SilentMotion(scenes[i].MotionPrompt), Listed(o.VideoModel.Ratios, o.Aspect),
                o.VideoResolution, o.VideoMode, PickDuration(o.VideoModel.Durations, scenes[i].Seconds), [images[i]], null, null), o.Project, $"video cảnh {i + 1}", ct));
        }

        var videos = await WaitAllAsync(videoTasks, o.Project, "video", log, TimeSpan.FromMinutes(30), ct);

        // 3. TodoX's edit: scenes in order, the narration read aloud with captions, a closing card.
        log("Dựng video: ghép cảnh, giọng đọc, phụ đề…");
        var progress = new Progress<ProgressNotificationValue>(p => { if (!string.IsNullOrWhiteSpace(p.Message)) log(p.Message); });
        var script = string.Join("\n", scenes.Select(s => s.Voice.Trim()).Where(v => v.Length > 0));
        var folder = _projects.Find(o.Project, create: true).Folder?.Path;
        var output = folder is null ? null : Path.Combine(folder, ProjectFolders.OutputDir, $"todox-san-pham-{DateTime.Now:yyyyMMdd-HHmmss}-{o.Aspect.Replace(':', 'x')}.mp4");
        var result = await _tools.AutoEdit(videos.ToArray(), o.Project, progress, ct, aspect: o.Aspect, targetSeconds: scenes.Sum(s => s.Seconds),
            script: script, voice: string.IsNullOrWhiteSpace(o.Voice) ? "default" : o.Voice, outroText: string.IsNullOrWhiteSpace(o.Outro) ? null : o.Outro.Trim(),
            output: output, skill: SkillSlug);
        return OutputOf(result) ?? throw new InvalidOperationException(result);
    }

    private async Task<string> SubmitAsync(AiCreateInput input, string project, string what, CancellationToken ct)
    {
        var answer = await _tools.SubmitAiAsync(input, project, ct);
        return TaskOf(answer) ?? throw new InvalidOperationException($"Không gửi được {what}: {answer}");
    }

    private async Task<List<string>> WaitAllAsync(List<string> tasks, string project, string kind, Action<string> log, TimeSpan limit, CancellationToken ct)
    {
        var files = new string?[tasks.Count];
        var deadline = DateTime.UtcNow + limit;
        while (files.Any(f => f is null))
        {
            for (var i = 0; i < tasks.Count; i++)
            {
                if (files[i] is not null)
                {
                    continue;
                }

                var answer = await _tools.PollAiAsync(tasks[i], project, ct);
                switch (StatusOf(answer))
                {
                    case "succeeded" when FileOf(answer) is { } file:
                        files[i] = file;
                        log($"Xong {kind} cảnh {i + 1}.");
                        break;
                    case "running" or "succeeded":
                        break;
                    default:
                        throw new InvalidOperationException($"Tạo {kind} cảnh {i + 1} không thành công: {MessageOf(answer) ?? answer}");
                }
            }

            if (files.All(f => f is not null))
            {
                break;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"AI tạo {kind} lâu quá. Các tác vụ vẫn chạy trên TodoX; xem lại trong mục Tạo video AI sau ít phút.");
            }

            log($"Đang chờ AI tạo {kind}: xong {files.Count(f => f is not null)}/{tasks.Count}…");
            await _delay(Poll, ct);
        }

        return files.Select(f => f!).ToList();
    }

    internal static string? TaskOf(string answer) => Json(answer) is { } e && e.TryGetProperty("task", out var t) ? t.ToString() : null;

    internal static string? StatusOf(string answer) => Json(answer) is { } e ? Str(e, "status") : null;

    internal static string? MessageOf(string answer) => Json(answer) is { } e ? Str(e, "message") : null;

    internal static string? FileOf(string answer) =>
        Json(answer) is { } e && e.TryGetProperty("files", out var f) && f.ValueKind == JsonValueKind.Array && f.GetArrayLength() > 0 ? f[0].GetString() : null;

    internal static string? OutputOf(string answer) =>
        Json(answer) is { } e && (Str(e, "output") ?? Str(e, "video") ?? Str(e, "path")) is { } path && File.Exists(path) ? path : null;

    private static JsonElement? Json(string text)
    {
        if (!text.TrimStart().StartsWith('{'))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int SecondsOf(string duration)
    {
        var digits = new string(duration.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var s) ? s : 0;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string> Strs(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList()
            : [];
}
