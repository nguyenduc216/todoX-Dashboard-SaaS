using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TodoX.EditVideoTool.Licensing;

namespace TodoX.EditVideoTool.Server;

public sealed record ServerResult<T>(T? Value, string? ErrorCode, string? Message)
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

    public TodoXServerClient(HttpClient http, LicenseStore store, IDeviceIdentity device)
    {
        _http = http;
        _store = store;
        _device = device;
    }

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
        SendAsync<JsonElement>(HttpMethod.Post, "tts", new { text, voiceCode, sessionId }, true, ct, TimeSpan.FromSeconds(60));

    public Task<ServerResult<JsonElement>> TtsStatusAsync(Guid jobId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Get, $"tts/{jobId}", null, true, ct, TimeSpan.FromSeconds(60));

    public Task<ServerResult<JsonElement>> AiModelsAsync(string? type, string? model, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Get, $"ai/models?type={Uri.EscapeDataString(type ?? string.Empty)}&model={Uri.EscapeDataString(model ?? string.Empty)}", null, true, ct, TimeSpan.FromSeconds(60));

    public Task<ServerResult<JsonElement>> AiCreateAsync(string kind, object request, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, kind == "video" ? "ai/videos" : "ai/images", request, true, ct, TimeSpan.FromSeconds(120));

    public Task<ServerResult<JsonElement>> AiTaskAsync(Guid taskId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Get, $"ai/tasks/{taskId}", null, true, ct, TimeSpan.FromSeconds(60));

    public Task<ServerResult<JsonElement>> AiTasksAsync(string? type, int limit, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Get, $"ai/tasks?type={Uri.EscapeDataString(type ?? string.Empty)}&limit={limit}", null, true, ct, TimeSpan.FromSeconds(120));

    public Task<ServerResult<JsonElement>> AiUploadAsync(string fileName, string base64, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, "ai/uploads", new { fileName, data = base64 }, true, ct, TimeSpan.FromSeconds(180));

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

    private async Task<ServerResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, bool authenticated, CancellationToken ct, TimeSpan? timeout = null)
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

        if (body is not null)
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
            return ServerResult<T>.Fail(code ?? $"http_{(int)response.StatusCode}", message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return ServerResult<T>.Fail(LicenseApiClient.NetworkError, "Không kết nối được máy chủ TodoX.");
        }
    }
}
