using System.Text.Json;

namespace TodoX.EditVideoTool.Server;

public sealed record StoredFileLink(string Url, string FileName, long SizeBytes, DateTimeOffset? ExpiresAt, long UsedBytes, long LimitBytes);

/// <summary>
/// Puts a file from this computer into the customer's space on the TodoX server and returns a public link, so the
/// AI provider (79AI) can read photos and clips by URL. Files stay 7 days and count against the package's storage.
/// Sent in 8 MB chunks.
/// </summary>
public sealed class StorageUploader
{
    public const int ChunkBytes = 8 * 1024 * 1024;
    public const long MaxFileBytes = 500L * 1024 * 1024;

    private readonly TodoXServerClient _server;

    public StorageUploader(TodoXServerClient server)
    {
        _server = server;
    }

    /// <summary>Returns the link, or an error message for the user.</summary>
    public async Task<(StoredFileLink? File, string? Error)> UploadAsync(string path, string project, IProgress<double>? progress, CancellationToken ct)
    {
        var full = Path.GetFullPath(path.Trim().Trim('"'));
        if (!File.Exists(full))
        {
            return (null, $"Không tìm thấy file: {full}");
        }

        var size = new FileInfo(full).Length;
        if (size == 0 || size > MaxFileBytes)
        {
            return (null, $"File {Path.GetFileName(full)} phải nhỏ hơn {MaxFileBytes / 1024 / 1024} MB.");
        }

        var upload = Guid.NewGuid();
        var buffer = new byte[ChunkBytes];
        await using var stream = File.OpenRead(full);
        long offset = 0;
        while (offset < size)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(read), ct);
                if (n == 0)
                {
                    break;
                }

                read += n;
            }

            var result = await _server.StorageChunkAsync(upload, project, Path.GetFileName(full), size, offset, buffer, read, ct);
            if (!result.Ok)
            {
                return (null, result.Message ?? "Không tải được file lên TodoX.");
            }

            offset += read;
            progress?.Report((double)offset / size);
            if (offset >= size)
            {
                return result.Value.TryGetProperty("file", out var file) ? (Read(file), null) : (null, "Máy chủ TodoX chưa trả về link file.");
            }
        }

        return (null, "Không tải được file lên TodoX.");
    }

    private static StoredFileLink Read(JsonElement file) => new(
        file.GetProperty("url").GetString()!,
        file.TryGetProperty("fileName", out var n) ? n.GetString() ?? "" : "",
        file.TryGetProperty("sizeBytes", out var s) ? s.GetInt64() : 0,
        file.TryGetProperty("expiresAt", out var e) && e.TryGetDateTimeOffset(out var at) ? at : null,
        file.TryGetProperty("usedBytes", out var u) ? u.GetInt64() : 0,
        file.TryGetProperty("limitBytes", out var l) ? l.GetInt64() : 0);

    public static string Size(long bytes) =>
        bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1024d / 1024 / 1024:0.##} GB" : $"{bytes / 1024d / 1024:0.#} MB";
}
