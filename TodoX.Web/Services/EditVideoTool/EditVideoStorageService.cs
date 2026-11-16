using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Options;
using TodoX.Web.Data;
using TodoX.Web.Services.SharedMedia;

namespace TodoX.Web.Services.EditVideoTool;

public sealed class EditVideoStorageOptions
{
    public const string SectionName = "EditVideoTool:Storage";

    /// <summary>Public address of the dashboard; the AI provider downloads files from here.</summary>
    public string PublicBaseUrl { get; set; } = "https://dashboard.todox.vn";
    /// <summary>Folder under SharedMedia:StorageRoot.</summary>
    public string Subfolder { get; set; } = "editvideo-files";
    public int KeepDays { get; set; } = 7;
    public long MaxFileBytes { get; set; } = 500L * 1024 * 1024;
    public int MaxChunkBytes { get; set; } = 8 * 1024 * 1024;
}

public sealed record EditVideoStoredFile(Guid Id, string Url, string FileName, long SizeBytes, DateTimeOffset ExpiresAt, long UsedBytes, long LimitBytes);

/// <summary>
/// Each customer's space on the TodoX server for files the AI provider (79AI) must read by link: photos and clips the
/// customer's TodoX EditVideo uploads from a project. Files are kept <see cref="EditVideoStorageOptions.KeepDays"/> days
/// and count against the customer's package quota (1 GB by default). Uploads come in chunks so large clips pass the
/// web server's request limit; links are unguessable.
/// </summary>
public sealed class EditVideoStorageService
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".mp4", ".mov", ".m4v", ".webm", ".mp3", ".wav", ".m4a"
    };

    private readonly TodoXConnectionFactory _factory;
    private readonly PackageEntitlementService _entitlements;
    private readonly EditVideoStorageOptions _options;
    private readonly SharedMediaOptions _media;
    private readonly TimeProvider _clock;

    public EditVideoStorageService(TodoXConnectionFactory factory, PackageEntitlementService entitlements,
        IOptions<EditVideoStorageOptions> options, IOptions<SharedMediaOptions> media, TimeProvider? clock = null)
    {
        _factory = factory;
        _entitlements = entitlements;
        _options = options.Value;
        _media = media.Value;
        _clock = clock ?? TimeProvider.System;
    }

    public string Root => Path.Combine(Path.GetFullPath(_media.StorageRoot ?? "storage"), _options.Subfolder);

    public async Task<long> UsedBytesAsync(Guid customerId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(
            "SELECT COALESCE(sum(size_bytes), 0)::bigint FROM editvideo.storage_files WHERE customer_id = @customerId AND deleted_at IS NULL AND expires_at > now();",
            new { customerId });
    }

    /// <summary>
    /// Appends one chunk of an upload. The first chunk (offset 0) checks the type, size and quota; the last one
    /// stores the file and returns its link. <paramref name="uploadId"/> is chosen by the app (a GUID per file).
    /// </summary>
    public async Task<EditVideoResult<EditVideoStoredFile?>> AppendAsync(EditVideoLicenseRow license, Guid uploadId, string? project,
        string? fileName, long totalSize, long offset, Stream body, CancellationToken ct = default)
    {
        var name = SafeFileName(fileName);
        var projectName = (project ?? string.Empty).Trim();
        if (name is null || !Allowed.Contains(Path.GetExtension(name)))
        {
            return Fail("Chỉ tải lên ảnh (.jpg .png .webp), video (.mp4 .mov .webm) hoặc âm thanh (.mp3 .wav .m4a).");
        }

        if (projectName.Length == 0 || projectName.Length > 120)
        {
            return Fail("Thiếu tên dự án.");
        }

        if (totalSize <= 0 || totalSize > _options.MaxFileBytes || offset < 0 || offset >= totalSize)
        {
            return Fail($"File phải nhỏ hơn {_options.MaxFileBytes / 1024 / 1024} MB.");
        }

        var parts = Path.Combine(Root, ".parts");
        Directory.CreateDirectory(parts);
        var partPath = Path.Combine(parts, $"{license.CustomerId:N}-{uploadId:N}.part");

        if (offset == 0)
        {
            var entitlement = await _entitlements.GetAsync(license.CustomerId, ct);
            var used = await UsedBytesAsync(license.CustomerId, ct);
            if (used + totalSize > entitlement.StorageBytes)
            {
                return EditVideoResult<EditVideoStoredFile?>.Fail("storage_full",
                    $"Hết dung lượng lưu trữ: đã dùng {Mb(used)} / {Mb(entitlement.StorageBytes)}. File cũ tự xoá sau {_options.KeepDays} ngày, hoặc nâng gói để có thêm dung lượng.");
            }

            File.Delete(partPath);
        }
        else if (!File.Exists(partPath) || new FileInfo(partPath).Length != offset)
        {
            return Fail("Lượt tải lên bị gián đoạn. Tải lại file từ đầu.");
        }

        long written;
        await using (var file = new FileStream(partPath, FileMode.Append, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            written = 0;
            int read;
            while ((read = await body.ReadAsync(buffer, ct)) > 0)
            {
                written += read;
                if (written > _options.MaxChunkBytes || offset + written > totalSize)
                {
                    file.Close();
                    File.Delete(partPath);
                    return Fail("Phần tải lên quá lớn.");
                }

                await file.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }

        if (offset + written < totalSize)
        {
            return EditVideoResult<EditVideoStoredFile?>.Success(null); // more chunks to come
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var relative = Path.Combine(token[..2], token, name);
        var finalPath = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        File.Move(partPath, finalPath);

        var now = _clock.GetUtcNow();
        var expires = now.AddDays(_options.KeepDays);
        var id = Guid.NewGuid();
        using (var conn = await _factory.OpenAsync(ct))
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO editvideo.storage_files
                    (id, tenant_id, customer_id, license_id, project_key, project_name, file_name, relative_path,
                     size_bytes, content_type, public_token, created_at, expires_at)
                VALUES (@id, @tenantId, @customerId, @licenseId, @projectKey, @projectName, @name, @relative,
                        @size, @contentType, @token, @now, @expires);
                """,
                new
                {
                    id, tenantId = license.TenantId, customerId = license.CustomerId, licenseId = license.Id,
                    projectKey = ProjectKey(projectName), projectName, name, relative = relative.Replace('\\', '/'),
                    size = totalSize, contentType = ContentType(name), token, now, expires
                });
        }

        var url = $"{_options.PublicBaseUrl.TrimEnd('/')}/{(_media.RequestPath ?? "/media").Trim('/')}/{_options.Subfolder}/{relative.Replace('\\', '/')}";
        var usedNow = await UsedBytesAsync(license.CustomerId, ct);
        var limit = (await _entitlements.GetAsync(license.CustomerId, ct)).StorageBytes;
        return EditVideoResult<EditVideoStoredFile?>.Success(new EditVideoStoredFile(id, Uri.EscapeUriString(url), name, totalSize, expires, usedNow, limit));
    }

    /// <summary>Deletes files past their 7 days and abandoned partial uploads; returns how many files went.</summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var expired = (await conn.QueryAsync<(Guid Id, string RelativePath)>(
            "SELECT id, relative_path FROM editvideo.storage_files WHERE deleted_at IS NULL AND expires_at <= now() ORDER BY expires_at LIMIT 500;")).ToList();
        foreach (var (id, relative) in expired)
        {
            var path = Path.GetFullPath(Path.Combine(Root, relative));
            if (path.StartsWith(Root, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                File.Delete(path);
                var dir = Path.GetDirectoryName(path)!;
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }

            await conn.ExecuteAsync("UPDATE editvideo.storage_files SET deleted_at = now() WHERE id = @id;", new { id });
        }

        var parts = Path.Combine(Root, ".parts");
        if (Directory.Exists(parts))
        {
            foreach (var part in Directory.EnumerateFiles(parts, "*.part"))
            {
                if (File.GetLastWriteTimeUtc(part) < DateTime.UtcNow.AddDays(-1))
                {
                    File.Delete(part);
                }
            }
        }

        return expired.Count;
    }

    /// <summary>Stable key of a project name: lower case, no accents, words joined by '-'.</summary>
    public static string ProjectKey(string name)
    {
        var normalized = name.Trim().Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsAsciiLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        var key = string.Join('-', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return key.Length > 80 ? key[..80] : key;
    }

    internal static string? SafeFileName(string? fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return null;
        }

        var ext = Path.GetExtension(name).ToLowerInvariant();
        var stem = ProjectKey(Path.GetFileNameWithoutExtension(name));
        if (stem.Length == 0)
        {
            stem = "file";
        }

        return stem + ext;
    }

    private static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".mp4" or ".m4v" => "video/mp4",
        ".mov" => "video/quicktime",
        ".webm" => "video/webm",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".m4a" => "audio/mp4",
        _ => "application/octet-stream"
    };

    private static string Mb(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1024d / 1024 / 1024:0.##} GB" : $"{bytes / 1024d / 1024:0.#} MB";

    private static EditVideoResult<EditVideoStoredFile?> Fail(string message) =>
        EditVideoResult<EditVideoStoredFile?>.Fail(EditVideoErrorCodes.InvalidRequest, message);
}

/// <summary>Every hour: removes EditVideo storage files older than their keep time.</summary>
public sealed class EditVideoStoragePurgeWorker(IServiceScopeFactory scopes, ILogger<EditVideoStoragePurgeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<TenantContext>().EnsureLoadedAsync();
                var removed = await scope.ServiceProvider.GetRequiredService<EditVideoStorageService>().PurgeExpiredAsync(stoppingToken);
                if (removed > 0)
                {
                    logger.LogInformation("EditVideo storage: removed {Count} expired files", removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "EditVideo storage purge failed");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
