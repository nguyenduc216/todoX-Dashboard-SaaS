using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TodoX.EditVideoTool.Licensing;

namespace TodoX.EditVideoTool.Server;

public sealed record ServerResult<T>(T? Value, string? ErrorCode, string? Message, JsonElement? Detail = null)
{
    public bool Ok => ErrorCode is null;
    public bool IsNetworkError => ErrorCode == LicenseApiClient.NetworkError;

    public static ServerResult<T> Fail(string code, string? message) => new(default, code, message);
}

/// <summary>
/// Authenticated calls to TodoX for an activated device: telemetry, edit plans, voices, releases.
/// Only small JSON is exchanged — never footage.
/// </summary>
public sealed class TodoXServerClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly LicenseStore _store;
    private readonly IDeviceIdentity _device;

    public TodoXServerClient(HttpClient http, LicenseStore store, IDeviceIdentity device, IPriceConsent? consent = null)
    {
        _http = http;
        _store = store;
        _device = device;
        Consent = consent;
    }

    /// <summary>Who is asked to accept TodoX's price before a paid command; null = never agree (the command is not run).</summary>
    public IPriceConsent? Consent { get; set; }

    /// <summary>The TodoX EditVideo window; null (tests) skips the check.</summary>
    public IAppWindow? Window { get; set; }

    public const string AppNotRunning = "app_not_running";

    /// <summary>The customer's "Luôn hỏi trước khi trừ điểm" setting; null (tests) = always ask.</summary>
    public Func<bool>? AskBeforeCharge { get; set; }

    /// <summary>How a charge without a window is logged at TodoX (the customer turned asking off).</summary>
    public const string NoAskMethod = "no_ask_setting";

    /// <summary>Shows "points used" when a project allowed its paid commands without asking; null in tests.</summary>
    public INotifier? Notifier { get; set; }

    private static readonly System.Globalization.CultureInfo Vi = new("vi-VN");

    /// <summary>A paid step for the project history: points, and the balance left after it when it was accepted.</summary>
    private static Projects.HistoryEntry Entry(PriceQuote q, string answer)
    {
        var charged = answer is Projects.ProjectHistory.Accepted or Projects.ProjectHistory.AcceptedForProject or Projects.ProjectHistory.Auto;
        return new(DateTimeOffset.Now, q.Kind, q.Summary, q.Points, q.Vnd, charged ? Math.Max(0, q.Balance - q.Points) : q.Balance, answer, q.QuoteId);
    }

    /// <summary>
    /// A paid command (AI image/video/music, voice). TodoX answers the first call with its price ("confirm_required");
    /// the person accepts or refuses it in the confirmation window, and only an accepted price is sent back — with the
    /// same request, so TodoX can check it is exactly what was priced. Prices are never taken from this app.
    /// </summary>
    private async Task<ServerResult<JsonElement>> SendPaidAsync(string path, object body, TimeSpan timeout, CancellationToken ct)
    {
        var payload = JsonSerializer.SerializeToNode(body, Json)!.AsObject();
        for (var round = 0; round < 3; round++)
        {
            var result = await SendAsync<JsonElement>(HttpMethod.Post, path, payload, true, ct, timeout);
            if (result.ErrorCode != ConfirmRequired || PriceQuote.From(result.Detail) is not { } quote)
            {
                return result;
            }

            var scope = Projects.PaidScope.Project;
            quote = quote with { Project = scope?.Name };
            string method;
            if (AskBeforeCharge is { } ask && !ask())
            {
                // Default: the customer does not want to be asked; charged straight away, kept in the project history.
                method = NoAskMethod;
                if (scope is { } quiet)
                {
                    Projects.ProjectHistory.Add(quiet.Dir, Entry(quote, Projects.ProjectHistory.Auto));
                }
            }
            else if (scope is { } granted && Projects.ProjectHistory.IsGranted(granted.Dir))
            {
                // The person allowed every paid command of this project: no window, only a notice of the points used.
                method = "project_grant";
                Projects.ProjectHistory.Add(granted.Dir, Entry(quote, Projects.ProjectHistory.Auto));
                Notifier?.Show($"TodoX · dự án {granted.Name}",
                    $"Trừ {quote.Points.ToString("0.##", Vi)} điểm (≈ {quote.Vnd.ToString("#,0", Vi)} đ), còn {Math.Max(0, quote.Balance - quote.Points).ToString("#,0.##", Vi)} điểm: {quote.Summary}");
            }
            else
            {
                // The price is confirmed with TodoX EditVideo open: opened here when the customer allowed it, otherwise
                // the assistant must ask the customer first (todox_app).
                if (Window is { IsRunning: false } window)
                {
                    if (!window.AutoOpen)
                    {
                        return ServerResult<JsonElement>.Fail(AppNotRunning,
                            "TodoX EditVideo chưa mở nên chưa hiện được cửa sổ xác nhận giá. Chưa tạo gì, không trừ điểm. " +
                            "Hỏi người dùng: \"Cho phép tôi mở TodoX EditVideo bây giờ và tự mở mỗi khi cần xác nhận giá về sau?\" " +
                            "Đồng ý: gọi todox_app với action=open, remember=true rồi gọi lại lệnh này. Chỉ lần này: action=open, remember=false. " +
                            "Không đồng ý: nhờ người dùng tự mở TodoX EditVideo rồi gọi lại.");
                    }

                    await window.OpenAsync(ct);
                }

                var answer = Consent is null ? ConsentAnswer.Unavailable : await Consent.AskAsync(quote, ct);
                if (scope is { } project)
                {
                    Projects.ProjectHistory.Add(project.Dir, Entry(quote, answer switch
                    {
                        ConsentAnswer.Accepted => Projects.ProjectHistory.Accepted,
                        ConsentAnswer.AcceptedForProject => Projects.ProjectHistory.AcceptedForProject,
                        ConsentAnswer.Declined => Projects.ProjectHistory.Declined,
                        ConsentAnswer.NoAnswer => Projects.ProjectHistory.NoAnswer,
                        _ => Projects.ProjectHistory.Unavailable
                    }));
                    if (answer == ConsentAnswer.AcceptedForProject)
                    {
                        Projects.ProjectHistory.Grant(project.Dir, Environment.UserName);
                    }
                }

                if (answer is not (ConsentAnswer.Accepted or ConsentAnswer.AcceptedForProject))
                {
                    // The assistant is told what happened, never the price (that is only for the person, in the window).
                    var price = "cho: " + quote.Summary;
                    return ServerResult<JsonElement>.Fail(PriceDeclined, answer switch
                    {
                        ConsentAnswer.Declined => $"Người dùng bấm Hủy ở cửa sổ xác nhận giá {price}. Chưa tạo gì, không trừ điểm. Không gọi lại trừ khi người dùng yêu cầu.",
                        ConsentAnswer.NoAnswer => $"Cửa sổ xác nhận giá {price} đã hiện nhưng không ai bấm trong {DesktopPriceConsent.Wait.TotalMinutes:0} phút. Chưa tạo gì, không trừ điểm. Hỏi người dùng có thấy cửa sổ \"TodoX EditVideo — Xác nhận trừ điểm\" không rồi mới gọi lại.",
                        _ => $"Không hiện được cửa sổ xác nhận giá {price} trên máy này. Chưa tạo gì, không trừ điểm. Báo người dùng mở TodoX EditVideo (cửa sổ chính) rồi thử lại."
                    });
                }

                method = answer == ConsentAnswer.AcceptedForProject ? "desktop_dialog_project" : Consent!.Method;
            }

            payload["confirmQuoteId"] = quote.QuoteId;
            payload["consentMethod"] = method;
            payload["consentUser"] = scope is { } p ? $"{Environment.UserName} | dự án: {p.Name}" : Environment.UserName;
        }

        return ServerResult<JsonElement>.Fail(ConfirmRequired, "Giá TodoX vừa thay đổi; hãy thử lại để xem giá mới.");
    }

    public const string ConfirmRequired = "confirm_required";
    public const string PriceDeclined = "price_declined";

    public string ServerUrl => (_store.Load()?.ServerUrl ?? LicenseApiClient.DefaultServerUrl).TrimEnd('/');

    public Task<ServerResult<JsonElement>> PingAsync(CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Get, "ping", null, authenticated: false, ct, TimeSpan.FromSeconds(8));

    public Task<ServerResult<JsonElement>> StartSessionAsync(string tool, string appVersion, string? client, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, "sessions", new { tool, appVersion, client }, true, ct);

    public Task<ServerResult<JsonElement>> SendLogsAsync(Guid sessionId, IEnumerable<object> entries, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"sessions/{sessionId}/logs", new { entries }, true, ct);

    public Task<ServerResult<JsonElement>> EndSessionAsync(Guid sessionId, string status, object? summary, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"sessions/{sessionId}/end", new { status, summary }, true, ct);

    public Task<ServerResult<JsonElement>> PlanAsync(object request, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, "plan", request, true, ct, TimeSpan.FromSeconds(30));

    public Task<ServerResult<JsonElement>> PlanEditAsync(object request, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, "plan/edit", request, true, ct, TimeSpan.FromSeconds(30));

    public Task<ServerResult<JsonElement>> SkillRunStartAsync(string skill, int? version, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, "skills/run", new { skill, version }, true, ct, TimeSpan.FromSeconds(30));

    public Task<ServerResult<JsonElement>> SkillRunNextAsync(string run, bool repeat, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, "skills/run/next", new { run, repeat }, true, ct, TimeSpan.FromSeconds(30));

    public Task<ServerResult<JsonElement>> StylesAsync(CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Get, "styles", null, false, ct);

    public Task<ServerResult<JsonElement>> VoicesAsync(CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Get, "voices", null, true, ct);

    public Task<ServerResult<JsonElement>> SubmitTtsAsync(string text, string? voiceCode, Guid? sessionId, CancellationToken ct = default) =>
        SendPaidAsync("tts", new { text, voiceCode, sessionId }, TimeSpan.FromSeconds(60), ct);

    public Task<ServerResult<JsonElement>> TtsStatusAsync(Guid jobId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Get, $"tts/{jobId}", null, true, ct, TimeSpan.FromSeconds(60));

    public Task<ServerResult<JsonElement>> AiModelsAsync(string? type, string? model, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Get, $"ai/models?type={Uri.EscapeDataString(type ?? string.Empty)}&model={Uri.EscapeDataString(model ?? string.Empty)}", null, true, ct, TimeSpan.FromSeconds(60));

    public Task<ServerResult<JsonElement>> AiCreateAsync(string kind, object request, CancellationToken ct = default) =>
        SendPaidAsync(kind == "video" ? "ai/videos" : "ai/images", request, TimeSpan.FromSeconds(120), ct);

    public Task<ServerResult<JsonElement>> AiMusicAsync(object request, CancellationToken ct = default) =>
        SendPaidAsync("ai/music", request, TimeSpan.FromSeconds(120), ct);

    public Task<ServerResult<JsonElement>> AiTaskAsync(Guid taskId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Get, $"ai/tasks/{taskId}", null, true, ct, TimeSpan.FromSeconds(60));

    public Task<ServerResult<JsonElement>> AiTasksAsync(string? type, int limit, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Get, $"ai/tasks?type={Uri.EscapeDataString(type ?? string.Empty)}&limit={limit}", null, true, ct, TimeSpan.FromSeconds(120));

    public Task<ServerResult<JsonElement>> AiUploadAsync(string fileName, string base64, CancellationToken ct = default, string? project = null) =>
        SendAsync<JsonElement>(HttpMethod.Post, "ai/uploads", new { fileName, data = base64, project }, true, ct, TimeSpan.FromSeconds(180));

    public Task<ServerResult<JsonElement>> SkillsAsync(CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Get, "skills", null, true, ct, TimeSpan.FromSeconds(30));

    public Task<ServerResult<JsonElement>> LatestReleaseAsync(CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Get, "releases/latest", null, false, ct, TimeSpan.FromSeconds(15));

    public async Task DownloadAsync(string url, string destination, IProgress<double>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        var tmp = destination + ".part";
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var target = File.Create(tmp))
        {
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total is > 0)
                {
                    progress?.Report(done * 100.0 / total.Value);
                }
            }
        }

        File.Move(tmp, destination, overwrite: true);
    }

    /// <summary>Project and exported-video information for "Jobs của tôi" (no files).</summary>
    public Task<ServerResult<JsonElement>> SyncProjectsAsync(object body, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, "projects/sync", body, true, ct, TimeSpan.FromSeconds(60));

    /// <summary>One chunk of a video being synced to the dashboard (at most 8 MB).</summary>
    public Task<ServerResult<JsonElement>> ProjectVideoChunkAsync(Guid videoId, long size, long offset, byte[] data, int count, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"projects/videos/{videoId}/chunk?size={size}&offset={offset}", null, true, ct, TimeSpan.FromMinutes(3),
            new ByteArrayContent(data, 0, count));

    /// <summary>The account's characters, voices, music and VFX models (kind: characters | voices | music | vfx | all).</summary>
    public Task<ServerResult<JsonElement>> LibraryAsync(string? kind, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Get, $"library?kind={Uri.EscapeDataString(kind ?? "all")}", null, true, ct, TimeSpan.FromSeconds(60));

    /// <summary>Package, its end date, points and storage of this machine's customer.</summary>
    public Task<ServerResult<JsonElement>> AccountAsync(CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Get, "account", null, true, ct, TimeSpan.FromSeconds(20));

    /// <summary>One chunk (at most 8 MB) of a file going to the customer's TodoX storage; the last one returns the link.</summary>
    public Task<ServerResult<JsonElement>> StorageChunkAsync(Guid upload, string project, string name, long size, long offset, byte[] data, int count, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post,
            $"files/chunk?upload={upload}&project={Uri.EscapeDataString(project)}&name={Uri.EscapeDataString(name)}&size={size}&offset={offset}",
            null, true, ct, TimeSpan.FromMinutes(3), new ByteArrayContent(data, 0, count));

    private async Task<ServerResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, bool authenticated, CancellationToken ct, TimeSpan? timeout = null, HttpContent? raw = null)
    {
        using var request = new HttpRequestMessage(method, $"{ServerUrl}/api/editvideo/v1/{path}");
        // Lets TodoX stop versions it no longer supports (release manifest "minSupported").
        request.Headers.Add("X-TodoX-App-Version", Status.StatusService.Version);
        if (authenticated)
        {
            var stored = _store.Load();
            if (stored is null)
            {
                return ServerResult<T>.Fail("not_activated", "Máy này chưa kích hoạt license.");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", stored.AccessToken);
            request.Headers.Add("X-TodoX-Device", _device.DeviceHash);
        }

        if (raw is not null)
        {
            request.Content = raw;
        }
        else if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(20));
        try
        {
            using var response = await _http.SendAsync(request, cts.Token);
            JsonElement envelope;
            try
            {
                envelope = await response.Content.ReadFromJsonAsync<JsonElement>(Json, cts.Token);
            }
            catch (JsonException)
            {
                return ServerResult<T>.Fail(LicenseApiClient.NetworkError, $"Máy chủ TodoX trả về dữ liệu không hợp lệ (HTTP {(int)response.StatusCode}).");
            }

            if (envelope.ValueKind == JsonValueKind.Object && envelope.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                var data = envelope.TryGetProperty("data", out var d) ? d : envelope;
                return new ServerResult<T>(data.Deserialize<T>(Json), null, null);
            }

            var code = envelope.ValueKind == JsonValueKind.Object && envelope.TryGetProperty("code", out var c) ? c.GetString() : null;
            var message = envelope.ValueKind == JsonValueKind.Object && envelope.TryGetProperty("message", out var m) ? m.GetString() : null;
            JsonElement? detail = envelope.ValueKind == JsonValueKind.Object && envelope.TryGetProperty("detail", out var dt) && dt.ValueKind == JsonValueKind.Object
                ? dt.Clone() : null;
            return new ServerResult<T>(default, code ?? $"http_{(int)response.StatusCode}", message, detail);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return ServerResult<T>.Fail(LicenseApiClient.NetworkError, "Không kết nối được máy chủ TodoX.");
        }
    }
}
