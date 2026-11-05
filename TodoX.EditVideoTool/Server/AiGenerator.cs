using System.Text.Json;
using TodoX.EditVideoTool.Licensing;

namespace TodoX.EditVideoTool.Server;

public sealed record AiCreateInput(
    string Kind, string Model, string? Prompt, string? Ratio, string? Resolution, string? Mode, string? Duration,
    string[]? Images, string[]? References, string[]? Videos);

/// <summary>
/// AI images/videos through TodoX: local input images are handed to TodoX (which passes them on without
/// storing), results are downloaded straight into the customer's folder. The assistant only ever gets TodoX
/// task ids and local file paths — never the generation provider's links.
/// </summary>
public sealed class AiGenerator
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] OutputExtensions = [".png", ".jpg", ".jpeg", ".webp", ".mp4", ".mov", ".webm"];

    private readonly TodoXServerClient _server;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public AiGenerator(TodoXServerClient server, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _server = server;
        _delay = delay ?? Task.Delay;
    }

    public static TimeSpan PollInterval { get; } = TimeSpan.FromSeconds(5);

    public async Task<string> ModelsAsync(string? kind, string? model, CancellationToken ct)
    {
        var models = await _server.AiModelsAsync(kind, model, ct);
        return models.Ok ? models.Value.GetRawText() : Explain(models);
    }

    public async Task<string> CreateAsync(AiCreateInput input, string folder, TimeSpan wait, Action<string>? progress, CancellationToken ct)
    {
        var images = await ResolveInputsAsync(input.Images, input.Kind, ct);
        if (images.Error is not null)
        {
            return images.Error;
        }

        var references = await ResolveInputsAsync(input.References, input.Kind, ct);
        if (references.Error is not null)
        {
            return references.Error;
        }

        var videos = await ResolveInputsAsync(input.Videos, "video-source", ct);
        if (videos.Error is not null)
        {
            return videos.Error;
        }

        progress?.Invoke("TodoX đang gửi yêu cầu tạo " + (input.Kind == "video" ? "video" : "ảnh"));
        var created = await _server.AiCreateAsync(input.Kind, new
        {
            model = input.Model,
            prompt = input.Prompt,
            ratio = input.Ratio,
            resolution = input.Resolution,
            mode = input.Mode,
            duration = input.Duration,
            images = images.Urls,
            references = references.Urls,
            videoUrls = videos.Urls,
            clientKey = "exe-" + Guid.NewGuid().ToString("N")
        }, ct);
        if (!created.Ok)
        {
            return Explain(created);
        }

        var task = created.Value;
        var deadline = DateTime.UtcNow + wait;
        while (Status(task) == "running" && DateTime.UtcNow < deadline)
        {
            progress?.Invoke(input.Kind == "video" ? "Đang chờ AI tạo video" : "Đang chờ AI tạo ảnh");
            await _delay(PollInterval, ct);
            var polled = await _server.AiTaskAsync(task.GetProperty("id").GetGuid(), ct);
            if (!polled.Ok)
            {
                return Explain(polled);
            }

            task = polled.Value;
        }

        return await ResultAsync(task, folder, ct);
    }

    public async Task<string> StatusAsync(string taskId, string folder, CancellationToken ct)
    {
        if (!Guid.TryParse(taskId, out var id))
        {
            return "Mã tác vụ không hợp lệ. Dùng todox_ai_tasks để xem các tác vụ gần đây.";
        }

        var task = await _server.AiTaskAsync(id, ct);
        return task.Ok ? await ResultAsync(task.Value, folder, ct) : Explain(task);
    }

    public async Task<string> TasksAsync(string? kind, int limit, CancellationToken ct)
    {
        var tasks = await _server.AiTasksAsync(kind, limit, ct);
        if (!tasks.Ok)
        {
            return Explain(tasks);
        }

        var list = tasks.Value.TryGetProperty("tasks", out var t) && t.ValueKind == JsonValueKind.Array ? t.EnumerateArray().ToList() : [];
        return JsonSerializer.Serialize(new
        {
            tasks = list.Select(x => new
            {
                task = x.GetProperty("id").GetGuid(),
                kind = Text(x, "kind"),
                model = Text(x, "model"),
                status = Status(x),
                points = x.TryGetProperty("points", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() : 0,
                createdAt = Text(x, "createdAt"),
                message = Text(x, "message")
            }),
            next = "Use todox_image_status / todox_video_status with a task to download a finished result into a folder."
        }, Json);
    }

    /// <summary>Shapes a task for the assistant; a finished task's files are downloaded first.</summary>
    internal async Task<string> ResultAsync(JsonElement task, string folder, CancellationToken ct)
    {
        var id = task.GetProperty("id").GetGuid();
        var kind = Text(task, "kind") ?? "image";
        var status = Status(task);
        var points = task.TryGetProperty("points", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() : 0;
        var files = new List<string>();
        if (status == "succeeded" && task.TryGetProperty("outputs", out var outputs) && outputs.ValueKind == JsonValueKind.Array)
        {
            var urls = outputs.EnumerateArray().Select(o => o.GetString()).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
            for (var i = 0; i < urls.Count; i++)
            {
                var path = Path.Combine(folder, FileName(id, kind, i, urls[i]!));
                if (!File.Exists(path))
                {
                    try
                    {
                        await _server.DownloadAsync(urls[i]!, path, null, ct);
                    }
                    catch (HttpRequestException)
                    {
                        return JsonSerializer.Serialize(new
                        {
                            task = id, kind, status, points,
                            message = "Kết quả đã xong nhưng tải về máy bị lỗi. Gọi lại tool trạng thái với task này để tải lại."
                        }, Json);
                    }
                }

                files.Add(path);
            }
        }

        return JsonSerializer.Serialize(new
        {
            task = id,
            kind,
            model = Text(task, "model"),
            status,
            points,
            files,
            message = Text(task, "message"),
            next = status == "running"
                ? $"Still generating. Call todox_{kind}_status with this task in about {(kind == "video" ? "a minute" : "20 seconds")}."
                : null
        }, Json);
    }

    internal async Task<(List<string> Urls, string? Error)> ResolveInputsAsync(string[]? items, string kind, CancellationToken ct)
    {
        var urls = new List<string>();
        foreach (var raw in (items ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()))
        {
            if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                if (uri.Scheme != Uri.UriSchemeHttps)
                {
                    return ([], $"Link đầu vào phải là https: {raw}");
                }

                urls.Add(raw);
                continue;
            }

            var path = Path.GetFullPath(raw);
            if (!File.Exists(path))
            {
                return ([], $"Không tìm thấy file: {path}");
            }

            if (kind == "video-source" || !ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                return ([], $"Chỉ ảnh .jpg/.png/.webp trên máy được dùng làm đầu vào; video đầu vào cần là link https công khai: {Path.GetFileName(path)}");
            }

            var size = new FileInfo(path).Length;
            if (size > MaxUploadBytes)
            {
                return ([], $"Ảnh {Path.GetFileName(path)} lớn hơn 10 MB; hãy dùng ảnh nhỏ hơn.");
            }

            var uploaded = await _server.AiUploadAsync(Path.GetFileName(path), Convert.ToBase64String(await File.ReadAllBytesAsync(path, ct)), ct);
            if (!uploaded.Ok)
            {
                return ([], Explain(uploaded));
            }

            urls.Add(uploaded.Value.GetProperty("url").GetString()!);
        }

        return (urls, null);
    }

    internal static string FileName(Guid task, string kind, int index, string url)
    {
        var ext = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() : string.Empty;
        if (!OutputExtensions.Contains(ext))
        {
            ext = kind == "video" ? ".mp4" : ".png";
        }

        return $"todox-ai-{kind}-{task.ToString("N")[..8]}{(index > 0 ? $"-{index + 1}" : string.Empty)}{ext}";
    }

    public static string DefaultFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TodoX", "ai");

    private static string Status(JsonElement task) => Text(task, "status") ?? "running";

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Explain<T>(ServerResult<T> r) => r.ErrorCode switch
    {
        LicenseApiClient.NetworkError => ServerConnectivity.OfflineText,
        "insufficient_points" or "invalid_request" or "ai_option_required" or "model_not_found" or "ai_service_unavailable"
            or "ai_failed" or "not_found" => r.Message ?? r.ErrorCode,
        _ => LicenseMessages.ForServerError(r.ErrorCode, r.Message)
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
