using System.Text.Json;
using System.Text.Json.Nodes;
using TodoX.EditVideoTool.Projects;

namespace TodoX.EditVideoTool.Server;

/// <summary>
/// The account's resources on the dashboard — AI characters, TodoX voices, music (TodoX library and the customer's
/// own AI songs) and the VFX models — copied to "_Thu vien TodoX" in the resource folder so videos can use them
/// offline. Refreshed when the app or a TodoX tool starts; files already downloaded are kept.
/// </summary>
public sealed class LibrarySync
{
    public const string FolderName = "_Thu vien TodoX";
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(15);

    private readonly TodoXServerClient _server;
    private readonly ProjectFolders _projects;
    private readonly HttpClient _http;
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LibrarySync(TodoXServerClient server, ProjectFolders projects, HttpClient http)
    {
        _server = server;
        _projects = projects;
        _http = http;
    }

    public string Folder => Path.Combine(_projects.Root, FolderName);
    private string IndexPath => Path.Combine(Folder, "thu-vien.json");

    /// <summary>The saved index (with local paths), refreshed from TodoX first when it is old or forced.</summary>
    public async Task<(JsonObject Index, string? Error)> GetAsync(bool refresh, CancellationToken ct = default)
    {
        string? error = null;
        if (refresh || DateTime.UtcNow - _lastRefresh > FreshFor || !File.Exists(IndexPath))
        {
            error = await RefreshAsync(ct);
        }

        try
        {
            return (File.Exists(IndexPath) ? JsonNode.Parse(await File.ReadAllTextAsync(IndexPath, ct))!.AsObject() : [], error);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return ([], error ?? ex.Message);
        }
    }

    /// <summary>Downloads the lists and their files; null when done, else why not (offline, not activated).</summary>
    public async Task<string?> RefreshAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var result = await _server.LibraryAsync("all", ct);
            if (!result.Ok)
            {
                return result.Message ?? result.ErrorCode;
            }

            var data = JsonNode.Parse(result.Value.GetRawText())!.AsObject();
            Directory.CreateDirectory(Folder);
            await DownloadAllAsync(data["characters"] as JsonArray, "imageUrl", "nhan-vat", c => $"{Safe(Str(c, "name"))}-{Str(c, "id")}", ct);
            await DownloadAllAsync(data["music"] as JsonArray, "url", "nhac", m => Safe(Str(m, "name")) + "-" + Str(m, "code"), ct);
            await DownloadAllAsync(data["voices"] as JsonArray, "previewUrl", "giong-doc-mau", v => Safe(Str(v, "code")), ct);
            data["updatedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            data["folder"] = Folder;
            await File.WriteAllTextAsync(IndexPath, data.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }), ct);
            _lastRefresh = DateTime.UtcNow;
            return null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or HttpRequestException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DownloadAllAsync(JsonArray? items, string urlKey, string sub, Func<JsonObject, string> name, CancellationToken ct)
    {
        if (items is null)
        {
            return;
        }

        var dir = Path.Combine(Folder, sub);
        Directory.CreateDirectory(dir);
        foreach (var item in items.OfType<JsonObject>())
        {
            if (Str(item, urlKey) is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                continue;
            }

            var ext = Path.GetExtension(uri.AbsolutePath);
            var file = Path.Combine(dir, name(item) + (string.IsNullOrEmpty(ext) || ext.Length > 5 ? ".bin" : ext.ToLowerInvariant()));
            if (!File.Exists(file))
            {
                try
                {
                    var bytes = await _http.GetByteArrayAsync(uri, ct);
                    await File.WriteAllBytesAsync(file, bytes, ct);
                }
                catch (HttpRequestException)
                {
                    continue; // one missing file must not stop the rest
                }
            }

            item["localPath"] = file;
        }
    }

    private static string? Str(JsonObject o, string k) => o[k] is JsonValue v
        ? v.TryGetValue<string>(out var s) ? s : v.TryGetValue<long>(out var l) ? l.ToString() : null
        : null;

    private static string Safe(string? value)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string((value ?? "file").Select(ch => bad.Contains(ch) ? '-' : ch).ToArray()).Trim().Trim('.');
        return clean.Length == 0 ? "file" : clean.Length > 60 ? clean[..60] : clean;
    }
}
