using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.ImageRender;

namespace TodoX.Web.Services.Media;

public sealed class MediaFileDto
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string FileCategory { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? MimeType { get; set; }
    public long? FileSizeBytes { get; set; }
    public string StorageProvider { get; set; } = "local";
    public string? ObjectKey { get; set; }
    public string? FileUrl { get; set; }
    public string? PublicUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public interface IMediaFileService
{
    /// <summary>Persist bytes to storage and insert a media.media_files row. Returns the new media id + public url.</summary>
    Task<MediaFileDto> SaveAsync(byte[] content, string originalFileName, string mimeType, string fileCategory,
        Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default);

    Task<MediaFileDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Read raw bytes for a media file (used to pass reference images to the render API).</summary>
    Task<byte[]?> ReadBytesAsync(Guid id, CancellationToken ct = default);

    /// <summary>Verify a media row belongs to the given user (ownership check).</summary>
    Task<bool> IsOwnedByAsync(Guid mediaId, Guid userId, CancellationToken ct = default);

    Task<ReferenceImage?> BuildReferenceImageAsync(Guid mediaId, string role, Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Sprint 2F media storage. Saves under wwwroot/uploads (local provider) and records metadata
/// in media.media_files. Designed so a MinIO provider can be swapped in later without UI changes.
/// </summary>
public sealed class MediaFileService : IMediaFileService
{
    private static readonly HashSet<string> AllowedMime = new(StringComparer.OrdinalIgnoreCase)
        { "image/png", "image/jpeg", "image/webp" };
    private const long MaxBytes = 10 * 1024 * 1024;

    private readonly TodoXConnectionFactory _factory;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<MediaFileService> _logger;

    public MediaFileService(TodoXConnectionFactory factory, IWebHostEnvironment env,
        IConfiguration config, ILogger<MediaFileService> logger)
    {
        _factory = factory;
        _env = env;
        _config = config;
        _logger = logger;
    }

    public async Task<MediaFileDto> SaveAsync(byte[] content, string originalFileName, string mimeType,
        string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
    {
        if (content.Length == 0) throw new InvalidOperationException("Tệp rỗng.");
        if (content.Length > MaxBytes) throw new InvalidOperationException("Tệp vượt quá 10MB.");
        if (!AllowedMime.Contains(mimeType)) throw new InvalidOperationException("Chỉ chấp nhận ảnh PNG, JPEG, WEBP.");

        var ext = mimeType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => Path.GetExtension(originalFileName)
        };

        // Safe, non-guessable file name; never reuse the client-supplied path.
        var id = Guid.NewGuid();
        var safeName = $"{id:N}{ext}";
        var relDir = Path.Combine(fileCategory, DateTime.UtcNow.ToString("yyyyMM"));
        var uploadRoot = _config["Storage:LocalUploadRoot"] ?? "wwwroot/uploads";
        var absDir = Path.Combine(_env.ContentRootPath, uploadRoot, relDir);
        Directory.CreateDirectory(absDir);
        var absPath = Path.Combine(absDir, safeName);
        await File.WriteAllBytesAsync(absPath, content, ct);

        var publicBase = _config["Storage:PublicUploadBase"] ?? "/uploads";
        var publicUrl = $"{publicBase}/{relDir.Replace('\\', '/')}/{safeName}";
        var objectKey = $"{relDir.Replace('\\', '/')}/{safeName}";

        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO media.media_files
                (id, tenant_id, customer_id, user_id, file_category, file_name, file_ext, mime_type,
                 file_size_bytes, storage_provider, object_key, file_url, public_url, is_active, created_at, created_by)
            VALUES
                (@id, @tenant, @customer, @user, @cat, @name, @ext, @mime,
                 @size, 'local', @key, @url, @url, true, now(), @user);
            """,
            new
            {
                id, tenant = tenantId, customer = customerId, user = userId, cat = fileCategory,
                name = safeName, ext, mime = mimeType, size = (long)content.Length, key = objectKey, url = publicUrl
            });

        _logger.LogInformation("Saved media {Id} category {Cat} ({Size} bytes)", id, fileCategory, content.Length);

        return new MediaFileDto
        {
            Id = id, UserId = userId, CustomerId = customerId, FileCategory = fileCategory,
            FileName = safeName, MimeType = mimeType, FileSizeBytes = content.Length,
            StorageProvider = "local", ObjectKey = objectKey, FileUrl = publicUrl, PublicUrl = publicUrl,
            IsActive = true, CreatedAt = DateTime.UtcNow
        };
    }

    public async Task<MediaFileDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<MediaFileDto>(
            """
            SELECT id AS Id, user_id AS UserId, customer_id AS CustomerId, file_category AS FileCategory,
                   file_name AS FileName, mime_type AS MimeType, file_size_bytes AS FileSizeBytes,
                   storage_provider AS StorageProvider, object_key AS ObjectKey, file_url AS FileUrl,
                   public_url AS PublicUrl, is_active AS IsActive, created_at AS CreatedAt
              FROM media.media_files WHERE id=@id;
            """, new { id });
    }

    public async Task<byte[]?> ReadBytesAsync(Guid id, CancellationToken ct = default)
    {
        var media = await GetAsync(id, ct);
        if (media?.ObjectKey is null) return null;
        var uploadRoot = _config["Storage:LocalUploadRoot"] ?? "wwwroot/uploads";
        var absPath = Path.Combine(_env.ContentRootPath, uploadRoot, media.ObjectKey.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(absPath) ? await File.ReadAllBytesAsync(absPath, ct) : null;
    }

    public async Task<ReferenceImage?> BuildReferenceImageAsync(Guid mediaId, string role, Guid userId, CancellationToken ct = default)
    {
        var media = await GetAsync(mediaId, ct);
        if (media is null || !media.IsActive)
        {
            throw new InvalidOperationException($"Khong tim thay anh tham chieu {role} hoac anh da bi vo hieu hoa.");
        }

        if (media.UserId is Guid owner && owner != userId)
        {
            throw new InvalidOperationException($"Anh tham chieu {role} khong thuoc ve nguoi dung hien tai.");
        }

        var bytes = await ReadBytesAsync(mediaId, ct);
        if (bytes is null || bytes.Length == 0)
        {
            throw new InvalidOperationException($"Da chon anh tham chieu {role} nhung he thong khong doc duoc noi dung tep.");
        }

        return new ReferenceImage
        {
            MediaId = media.Id,
            Role = role,
            MimeType = media.MimeType,
            Bytes = bytes,
            Base64 = Convert.ToBase64String(bytes),
            Url = media.PublicUrl ?? media.FileUrl,
            FileName = media.FileName
        };
    }

    public async Task<bool> IsOwnedByAsync(Guid mediaId, Guid userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM media.media_files WHERE id=@id AND user_id=@uid);",
            new { id = mediaId, uid = userId });
    }
}
