using System.Net.Http.Headers;
using System.Text.Json;
using TodoX.ReupVideo.Storage;

namespace TodoX.ReupVideo.Facebook;

public sealed record FacebookPageInfo(string Id, string Name, string? AccessToken);

public sealed record FacebookPostResult(bool Ok, string? Id, string? Error, FacebookPostKind UsedKind);

public interface IFacebookPublisher
{
    Task<FacebookPostResult> PublishAsync(string pageId, string pageToken, FacebookPostKind kind, string videoFile, string description, double? durationSeconds, CancellationToken ct);
}

/// <summary>
/// Facebook Graph API with a Page access token (needs pages_manage_posts + pages_read_engagement, and
/// publish_video for video). Reels: 3-phase upload (start → binary to rupload → finish/publish).
/// </summary>
public sealed class FacebookClient : IFacebookPublisher
{
    public const string GraphVersion = "v23.0";
    private const string Graph = "https://graph.facebook.com/" + GraphVersion;
    private const string GraphVideo = "https://graph-video.facebook.com/" + GraphVersion;

    /// <summary>Facebook Reels must be 3–90 seconds; longer clips are posted as a normal video.</summary>
    public const double ReelsMaxSeconds = 90;

    private readonly HttpClient _http;

    public FacebookClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>Checks a page token: returns the page it belongs to.</summary>
    public async Task<FacebookPageInfo> VerifyPageAsync(string pageIdOrMe, string token, CancellationToken ct)
    {
        var id = string.IsNullOrWhiteSpace(pageIdOrMe) ? "me" : pageIdOrMe.Trim();
        var json = await GetAsync($"{Graph}/{Uri.EscapeDataString(id)}?fields=id,name", token, ct);
        return new FacebookPageInfo(Str(json, "id") ?? id, Str(json, "name") ?? id, token);
    }

    /// <summary>From a user access token: every page the user manages, each with its own page token.</summary>
    public async Task<List<FacebookPageInfo>> ListPagesAsync(string userToken, CancellationToken ct)
    {
        var pages = new List<FacebookPageInfo>();
        string? next = $"{Graph}/me/accounts?fields=id,name,access_token&limit=100";
        while (next is not null && pages.Count < 1000)
        {
            var json = await GetAsync(next, userToken, ct);
            if (json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var page in data.EnumerateArray())
                {
                    if (Str(page, "id") is { } pid)
                    {
                        pages.Add(new FacebookPageInfo(pid, Str(page, "name") ?? pid, Str(page, "access_token")));
                    }
                }
            }

            next = json.TryGetProperty("paging", out var paging) ? Str(paging, "next") : null;
        }

        return pages;
    }

    public async Task<FacebookPostResult> PublishAsync(string pageId, string pageToken, FacebookPostKind kind, string videoFile, string description, double? durationSeconds, CancellationToken ct)
    {
        var useReels = kind == FacebookPostKind.Reels && (durationSeconds is null || durationSeconds <= ReelsMaxSeconds);
        try
        {
            return useReels
                ? await PublishReelAsync(pageId, pageToken, videoFile, description, ct)
                : await PublishVideoAsync(pageId, pageToken, videoFile, description, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new FacebookPostResult(false, null, ex.Message, useReels ? FacebookPostKind.Reels : FacebookPostKind.Video);
        }
    }

    private async Task<FacebookPostResult> PublishVideoAsync(string pageId, string token, string file, string description, CancellationToken ct)
    {
        await using var stream = File.OpenRead(file);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "access_token" },
            { new StringContent(description), "description" },
            { new StringContent("true"), "published" }
        };
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(fileContent, "source", Path.GetFileName(file));

        using var response = await _http.PostAsync($"{GraphVideo}/{pageId}/videos", form, ct);
        var json = await ReadAsync(response, ct);
        return new FacebookPostResult(true, Str(json, "id"), null, FacebookPostKind.Video);
    }

    private async Task<FacebookPostResult> PublishReelAsync(string pageId, string token, string file, string description, CancellationToken ct)
    {
        // 1) start: get a video id and an upload url
        using var start = await _http.PostAsync($"{Graph}/{pageId}/video_reels", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["upload_phase"] = "start",
            ["access_token"] = token
        }), ct);
        var startJson = await ReadAsync(start, ct);
        var videoId = Str(startJson, "video_id") ?? throw new InvalidOperationException("Facebook không trả video_id cho Reels");
        var uploadUrl = Str(startJson, "upload_url") ?? $"https://rupload.facebook.com/video-upload/{GraphVersion}/{videoId}";

        // 2) upload the bytes
        var size = new FileInfo(file).Length;
        await using (var stream = File.OpenRead(file))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl) { Content = new StreamContent(stream) };
            request.Headers.TryAddWithoutValidation("Authorization", "OAuth " + token);
            request.Headers.TryAddWithoutValidation("offset", "0");
            request.Headers.TryAddWithoutValidation("file_size", size.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var upload = await _http.SendAsync(request, ct);
            await ReadAsync(upload, ct);
        }

        // 3) finish + publish with the caption
        using var finish = await _http.PostAsync($"{Graph}/{pageId}/video_reels", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["upload_phase"] = "finish",
            ["video_id"] = videoId,
            ["video_state"] = "PUBLISHED",
            ["description"] = description,
            ["access_token"] = token
        }), ct);
        var finishJson = await ReadAsync(finish, ct);
        if (finishJson.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
        {
            throw new InvalidOperationException("Facebook từ chối đăng Reels");
        }

        return new FacebookPostResult(true, Str(finishJson, "post_id") ?? videoId, null, FacebookPostKind.Reels);
    }

    private async Task<JsonElement> GetAsync(string url, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // Token in a header, not the query string, so it never lands in proxy/server logs.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, ct);
        return await ReadAsync(response, ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        JsonElement json;
        try
        {
            json = JsonDocument.Parse(body.Length == 0 ? "{}" : body).RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"Facebook trả về HTTP {(int)response.StatusCode}: {Truncate(body)}");
        }

        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException(DescribeError(error));
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Facebook trả về HTTP {(int)response.StatusCode}: {Truncate(body)}");
        }

        return json;
    }

    /// <summary>Error text from <see cref="DescribeError"/> that means the token is expired or wrong (code 190).</summary>
    public static bool IsTokenError(string? error) => error is not null && error.Contains("(mã 190)", StringComparison.Ordinal);

    internal static string DescribeError(JsonElement error)
    {
        var message = Str(error, "error_user_msg") ?? Str(error, "message") ?? "Lỗi Facebook";
        var code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
        var hint = code switch
        {
            190 => " → Token hết hạn hoặc sai: lấy token mới và cập nhật fanpage.",
            200 or 10 => " → Token thiếu quyền (cần pages_manage_posts, pages_read_engagement, publish_video).",
            368 => " → Facebook tạm chặn đăng do nghi spam: giảm số video mỗi lần / tăng khoảng nghỉ.",
            _ => ""
        };
        return $"{message}{(code > 0 ? $" (mã {code})" : "")}{hint}";
    }

    private static string Truncate(string s) => s.Length > 200 ? s[..200] + "…" : s;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}
