using Dapper;
using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
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

    /// <summary>Persist bytes at an immutable caller-provided object key and insert a media.media_files row.</summary>
    Task<MediaFileDto> SaveAtObjectKeyAsync(byte[] content, string objectKey, string originalFileName, string mimeType, string fileCategory,
        Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default);

    Task<MediaFileDto?> GetAsync(Guid id, CancellationToken ct = default);

    Task<MediaFileDto?> GetByObjectKeyAsync(string objectKey, CancellationToken ct = default);
    Task<MediaFileDto?> GetByObjectKeyAsync(Guid tenantId, string objectKey, CancellationToken ct = default);

    Task<MediaFileDto?> GetByPublicUrlAsync(string publicUrl, CancellationToken ct = default);

    /// <summary>Read raw bytes for a media file (used to pass reference images to the render API).</summary>
    Task<byte[]?> ReadBytesAsync(Guid id, CancellationToken ct = default);

    /// <summary>Open raw media content as a read stream. Caller owns the returned stream.</summary>
    Task<Stream?> OpenReadAsync(Guid id, CancellationToken ct = default);

    Task<MediaFileDto> ReplaceContentAsync(Guid mediaId, byte[] content, string mimeType,
        Guid userId, CancellationToken ct = default);

    /// <summary>Verify a media row belongs to the given user (ownership check).</summary>
    Task<bool> IsOwnedByAsync(Guid mediaId, Guid userId, CancellationToken ct = default);

    Task<ReferenceImage?> BuildReferenceImageAsync(Guid mediaId, string role, Guid userId,
        bool enforceOwnership = true, CancellationToken ct = default);

    Task<MediaFileDto> DownloadAndSaveImageAsync(string imageUrl, string fileCategory,
        Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default);

    Task<MediaFileDto> DownloadAndSaveImageAtObjectKeyAsync(string imageUrl, string objectKey, string fileCategory,
        Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default);

    Task<MediaFileDto> SaveBinaryAtObjectKeyAsync(byte[] content, string objectKey, string originalFileName, string mimeType, string fileCategory,
        Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default);

    Task<MediaFileDto> DownloadAndSaveBinaryAtObjectKeyAsync(string fileUrl, string objectKey, string fileCategory, string expectedMimeType,
        Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// Sprint 2F media storage. Saves under wwwroot/uploads (local provider) and records metadata
/// in media.media_files. Designed so a MinIO provider can be swapped in later without UI changes.
/// </summary>
public sealed class MediaFileService : IMediaFileService
{
    private static readonly HashSet<string> AllowedMime = new(StringComparer.OrdinalIgnoreCase)
        { "image/png", "image/jpeg", "image/webp", "video/mp4", "audio/mpeg", "audio/mp3", "audio/wav", "audio/x-wav", "audio/mp4", "audio/m4a", "application/octet-stream" };

    private readonly TodoXConnectionFactory _factory;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<MediaFileService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public MediaFileService(TodoXConnectionFactory factory, IWebHostEnvironment env,
        IConfiguration config, ILogger<MediaFileService> logger, IHttpClientFactory httpClientFactory)
    {
        _factory = factory;
        _env = env;
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<MediaFileDto> SaveAsync(byte[] content, string originalFileName, string mimeType,
        string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
    {
        mimeType = NormalizeMimeType(mimeType, originalFileName);
        fileCategory = NormalizeDbText(fileCategory, "media");
        if (content.Length == 0) throw new InvalidOperationException("Tá»‡p rá»—ng.");
        if (content.Length > GetMaxImageBytes()) throw new InvalidOperationException("Tá»‡p vÆ°á»£t quÃ¡ 10MB.");
        if (!IsAllowedImageMime(mimeType)) throw new InvalidOperationException("Chá»‰ cháº¥p nháº­n áº£nh PNG, JPEG, WEBP.");

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
        var metadata = ReadImageMetadata(content, mimeType);
        var storageProvider = NormalizeDbText(_config["Storage:Provider"] ?? "local", "local");

        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO media.media_files
                (id, tenant_id, customer_id, user_id, file_category, file_name, file_ext, mime_type,
                 file_size_bytes, storage_provider, object_key, file_url, public_url, is_active, created_at, created_by)
            VALUES
                (@id, @tenant, @customer, @user, @cat, @name, @ext, @mime,
                 @size, @storage, @key, @url, @url, true, now(), @user);
            """,
            new
            {
                id,
                tenant = tenantId,
                customer = customerId,
                user = userId,
                cat = fileCategory,
                name = safeName,
                ext,
                mime = mimeType,
                size = (long)content.Length,
                key = objectKey,
                url = publicUrl,
                storage = storageProvider
            });

        _logger.LogInformation(
            "REFERENCE_IMAGE_STORED id={Id} category={Cat} file={FileName} mime={MimeType} size={Size} width={Width} height={Height} hasAlpha={HasAlpha} objectKey={ObjectKey} publicUrl={PublicUrl}",
            id, fileCategory, safeName, mimeType, content.Length, metadata.Width, metadata.Height, metadata.HasAlpha, objectKey, publicUrl);

        return new MediaFileDto
        {
            Id = id,
            UserId = userId,
            CustomerId = customerId,
            FileCategory = fileCategory,
            FileName = safeName,
            MimeType = mimeType,
            FileSizeBytes = content.Length,
            StorageProvider = storageProvider,
            ObjectKey = objectKey,
            FileUrl = publicUrl,
            PublicUrl = publicUrl,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public async Task<MediaFileDto> SaveAtObjectKeyAsync(byte[] content, string objectKey, string originalFileName, string mimeType,
        string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
    {
        mimeType = NormalizeMimeType(mimeType, originalFileName);
        fileCategory = NormalizeDbText(fileCategory, "media");
        if (content.Length == 0) throw new InvalidOperationException("Tá»‡p rá»—ng.");
        if (content.Length > GetMaxBytesForMime(mimeType)) throw new InvalidOperationException("Tá»‡p vÆ°á»£t quÃ¡ 10MB.");
        if (!IsPersistableMime(mimeType)) throw new InvalidOperationException("Chá»‰ cháº¥p nháº­n media há»£p lá»‡.");

        objectKey = NormalizeObjectKey(objectKey);
        var existingMedia = await TryGetExistingImmutableMediaAsync(objectKey, tenantId, mimeType, fileCategory, ct);
        if (existingMedia is not null)
        {
            return existingMedia;
        }

        var ext = ContentTypeToExtension(mimeType);
        var safeName = Path.GetFileName(objectKey);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = $"{Guid.NewGuid():N}{ext}";
        }

        var uploadRoot = _config["Storage:LocalUploadRoot"] ?? "wwwroot/uploads";
        var absRoot = Path.GetFullPath(Path.Combine(_env.ContentRootPath, uploadRoot));
        var absPath = Path.GetFullPath(Path.Combine(absRoot, objectKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!absPath.StartsWith(absRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage key khÃ´ng há»£p lá»‡.");
        }

        var absDir = Path.GetDirectoryName(absPath) ?? absRoot;
        Directory.CreateDirectory(absDir);
        var tempPath = Path.Combine(absDir, $".{Guid.NewGuid():N}.tmp");
        var publicBase = _config["Storage:PublicUploadBase"] ?? "/uploads";
        var publicUrl = $"{publicBase.TrimEnd('/')}/{objectKey}";
        var id = Guid.NewGuid();
        var storageProvider = NormalizeDbText(_config["Storage:Provider"] ?? "local", "local");
        try
        {
            await File.WriteAllBytesAsync(tempPath, content, ct);
            if (File.Exists(absPath))
            {
                TryDeleteFile(tempPath);
                var existing = await GetByObjectKeyAsync(tenantId, objectKey, ct);
                if (existing is not null)
                {
                    EnsureExistingMediaMatches(existing, mimeType, fileCategory);
                    return existing;
                }

                throw new InvalidOperationException("RVIDEO_MEDIA_FILE_WITHOUT_DB_RECORD: physical media exists without a tenant media row.");
            }
            File.Move(tempPath, absPath);

            using var conn = await _factory.OpenAsync(ct);
            await conn.ExecuteAsync(
                """
                INSERT INTO media.media_files
                    (id, tenant_id, customer_id, user_id, file_category, file_name, file_ext, mime_type,
                     file_size_bytes, storage_provider, object_key, file_url, public_url, is_active, created_at, created_by)
                VALUES
                    (@id, @tenant, @customer, @user, @cat, @name, @ext, @mime,
                     @size, @storage, @key, @url, @url, true, now(), @user);
                """,
                new
                {
                    id,
                    tenant = tenantId,
                    customer = customerId,
                    user = userId,
                    cat = fileCategory,
                    name = safeName,
                    ext,
                    mime = mimeType,
                    size = (long)content.Length,
                    storage = storageProvider,
                    key = objectKey,
                    url = publicUrl
                });
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }

        return new MediaFileDto
        {
            Id = id,
            UserId = userId,
            CustomerId = customerId,
            FileCategory = fileCategory,
            FileName = safeName,
            MimeType = mimeType,
            FileSizeBytes = content.Length,
            StorageProvider = storageProvider,
            ObjectKey = objectKey,
            FileUrl = publicUrl,
            PublicUrl = publicUrl,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
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

    public async Task<MediaFileDto?> GetByObjectKeyAsync(string objectKey, CancellationToken ct = default)
    {
        objectKey = NormalizeObjectKey(objectKey);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<MediaFileDto>(
            """
            SELECT id AS Id, user_id AS UserId, customer_id AS CustomerId, file_category AS FileCategory,
                   file_name AS FileName, mime_type AS MimeType, file_size_bytes AS FileSizeBytes,
                   storage_provider AS StorageProvider, object_key AS ObjectKey, file_url AS FileUrl,
                   public_url AS PublicUrl, is_active AS IsActive, created_at AS CreatedAt
              FROM media.media_files
             WHERE object_key=@objectKey
             ORDER BY created_at DESC
             LIMIT 1;
            """, new { objectKey });
    }

    public async Task<MediaFileDto?> GetByObjectKeyAsync(Guid tenantId, string objectKey, CancellationToken ct = default)
    {
        objectKey = NormalizeObjectKey(objectKey);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<MediaFileDto>(
            """
            SELECT id AS Id, user_id AS UserId, customer_id AS CustomerId, file_category AS FileCategory,
                   file_name AS FileName, mime_type AS MimeType, file_size_bytes AS FileSizeBytes,
                   storage_provider AS StorageProvider, object_key AS ObjectKey, file_url AS FileUrl,
                   public_url AS PublicUrl, is_active AS IsActive, created_at AS CreatedAt
              FROM media.media_files
             WHERE tenant_id=@tenantId
               AND object_key=@objectKey
             ORDER BY created_at DESC
             LIMIT 1;
            """, new { tenantId, objectKey });
    }

    public async Task<MediaFileDto?> GetByPublicUrlAsync(string publicUrl, CancellationToken ct = default)
    {
        var normalized = publicUrl.Trim();
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<MediaFileDto>(
            """
            SELECT id AS Id, user_id AS UserId, customer_id AS CustomerId, file_category AS FileCategory,
                   file_name AS FileName, mime_type AS MimeType, file_size_bytes AS FileSizeBytes,
                   storage_provider AS StorageProvider, object_key AS ObjectKey, file_url AS FileUrl,
                   public_url AS PublicUrl, is_active AS IsActive, created_at AS CreatedAt
              FROM media.media_files
             WHERE public_url=@normalized OR file_url=@normalized
             ORDER BY created_at DESC
             LIMIT 1;
            """, new { normalized });
    }

    public async Task<byte[]?> ReadBytesAsync(Guid id, CancellationToken ct = default)
    {
        var media = await GetAsync(id, ct);
        if (media?.ObjectKey is null) return null;
        var absPath = ResolveLocalPath(media.ObjectKey);
        return File.Exists(absPath) ? await File.ReadAllBytesAsync(absPath, ct) : null;
    }

    public async Task<Stream?> OpenReadAsync(Guid id, CancellationToken ct = default)
    {
        var media = await GetAsync(id, ct);
        if (media?.ObjectKey is null) return null;
        var absPath = ResolveLocalPath(media.ObjectKey);
        if (!File.Exists(absPath))
        {
            return null;
        }

        await Task.CompletedTask;
        return new FileStream(absPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024, useAsync: true);
    }

    public async Task<MediaFileDto> ReplaceContentAsync(Guid mediaId, byte[] content, string mimeType,
        Guid userId, CancellationToken ct = default)
    {
        var media = await GetAsync(mediaId, ct)
            ?? throw new InvalidOperationException("KhÃ´ng tÃ¬m tháº¥y áº£nh cáº§n lÆ°u.");
        if (media.UserId is Guid owner && owner != userId)
        {
            throw new InvalidOperationException("Báº¡n khÃ´ng cÃ³ quyá»n sá»­a áº£nh nÃ y.");
        }

        mimeType = NormalizeMimeType(mimeType, media.FileName);
        if (content.Length == 0) throw new InvalidOperationException("Tá»‡p rá»—ng.");
        if (content.Length > GetMaxImageBytes()) throw new InvalidOperationException("Tá»‡p vÆ°á»£t quÃ¡ 10MB.");
        if (!IsAllowedImageMime(mimeType)) throw new InvalidOperationException("Chá»‰ cháº¥p nháº­n áº£nh PNG, JPEG, WEBP.");
        if (string.IsNullOrWhiteSpace(media.ObjectKey))
        {
            throw new InvalidOperationException("áº¢nh khÃ´ng cÃ³ Ä‘Æ°á»ng dáº«n lÆ°u trá»¯ Ä‘á»ƒ ghi Ä‘Ã¨.");
        }

        var uploadRoot = _config["Storage:LocalUploadRoot"] ?? "wwwroot/uploads";
        var absPath = Path.Combine(_env.ContentRootPath, uploadRoot, media.ObjectKey.Replace('/', Path.DirectorySeparatorChar));
        var absDir = Path.GetDirectoryName(absPath);
        if (!string.IsNullOrWhiteSpace(absDir))
        {
            Directory.CreateDirectory(absDir);
        }

        await File.WriteAllBytesAsync(absPath, content, ct);

        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE media.media_files
               SET mime_type=@mime, file_size_bytes=@size
             WHERE id=@id;
            """,
            new { id = mediaId, mime = mimeType, size = (long)content.Length, user = userId });

        media.MimeType = mimeType;
        media.FileSizeBytes = content.Length;
        return media;
    }

    public async Task<ReferenceImage?> BuildReferenceImageAsync(Guid mediaId, string role, Guid userId,
        bool enforceOwnership = true, CancellationToken ct = default)
    {
        var media = await GetAsync(mediaId, ct);
        if (media is null || !media.IsActive)
        {
            throw new InvalidOperationException($"Khong tim thay anh tham chieu {role} hoac anh da bi vo hieu hoa.");
        }

        if (enforceOwnership && media.UserId is Guid owner && owner != userId)
        {
            throw new InvalidOperationException($"Anh tham chieu {role} khong thuoc ve nguoi dung hien tai.");
        }

        var bytes = await ReadBytesAsync(mediaId, ct);
        if (bytes is null || bytes.Length == 0)
        {
            throw new InvalidOperationException($"Da chon anh tham chieu {role} nhung he thong khong doc duoc noi dung tep.");
        }
        var metadata = ReadImageMetadata(bytes, media.MimeType ?? string.Empty);

        return new ReferenceImage
        {
            MediaId = media.Id,
            Role = role,
            MimeType = media.MimeType,
            Bytes = bytes,
            SizeBytes = bytes.Length,
            Width = metadata.Width,
            Height = metadata.Height,
            HasAlpha = metadata.HasAlpha,
            ObjectKey = media.ObjectKey,
            SourceType = media.FileCategory.Contains("_url", StringComparison.OrdinalIgnoreCase) ? "url" : "upload",
            SourceUrl = media.FileCategory.Contains("_url", StringComparison.OrdinalIgnoreCase) ? media.FileUrl : null,
            Base64 = Convert.ToBase64String(bytes),
            Url = media.PublicUrl ?? media.FileUrl,
            FileName = media.FileName,
            DisplayName = media.FileName,
            PromptRoleDescription = role
        };
    }

    public async Task<bool> IsOwnedByAsync(Guid mediaId, Guid userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM media.media_files WHERE id=@id AND user_id=@uid);",
            new { id = mediaId, uid = userId });
    }

    public async Task<MediaFileDto> DownloadAndSaveImageAsync(string imageUrl, string fileCategory,
        Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
    {
        var (bytes, contentType, fileName, uri) = await DownloadImageBytesAsync(imageUrl, ct);
        var saved = await SaveAsync(bytes, fileName, contentType, fileCategory,
            userId, customerId, tenantId, ct);
        _logger.LogInformation("MEDIA_IMAGE_URL_DOWNLOAD_SUCCESS url={Url} mediaId={MediaId} mime={MimeType} size={Size}",
            uri, saved.Id, saved.MimeType, saved.FileSizeBytes);
        return saved;
    }

    public async Task<MediaFileDto> DownloadAndSaveImageAtObjectKeyAsync(string imageUrl, string objectKey, string fileCategory,
        Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
    {
        var (bytes, contentType, fileName, uri) = await DownloadImageBytesAsync(imageUrl, ct);
        var saved = await SaveAtObjectKeyAsync(bytes, objectKey, fileName, contentType, fileCategory,
            userId, customerId, tenantId, ct);
        _logger.LogInformation("MEDIA_IMAGE_URL_DOWNLOAD_SUCCESS url={Url} mediaId={MediaId} mime={MimeType} size={Size}",
            uri, saved.Id, saved.MimeType, saved.FileSizeBytes);
        return saved;
    }

    public Task<MediaFileDto> SaveBinaryAtObjectKeyAsync(byte[] content, string objectKey, string originalFileName, string mimeType,
        string fileCategory, Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
        => SaveAtObjectKeyAsync(content, objectKey, originalFileName, mimeType, fileCategory, userId, customerId, tenantId, ct);

    public async Task<MediaFileDto> DownloadAndSaveBinaryAtObjectKeyAsync(string fileUrl, string objectKey, string fileCategory, string expectedMimeType,
        Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default)
    {
        objectKey = NormalizeObjectKey(objectKey);
        var expectedCategory = NormalizeDbText(fileCategory, "media");
        var expectedMedia = await TryGetExistingImmutableMediaAsync(
            objectKey,
            tenantId,
            NormalizeMimeType(expectedMimeType, objectKey),
            expectedCategory,
            ct);
        if (expectedMedia is not null)
        {
            return expectedMedia;
        }

        var saved = await DownloadBinaryToObjectKeyAsync(fileUrl, objectKey, fileCategory, expectedMimeType,
            userId, customerId, tenantId, ct);
        _logger.LogInformation("MEDIA_BINARY_URL_DOWNLOAD_SUCCESS url={Url} mediaId={MediaId} mime={MimeType} size={Size}",
            fileUrl, saved.Id, saved.MimeType, saved.FileSizeBytes);
        return saved;
    }

    private async Task<(byte[] Bytes, string ContentType, string FileName, Uri Uri)> DownloadImageBytesAsync(string imageUrl, CancellationToken ct)
    {
        var uri = ValidatePublicImageUri(imageUrl);
        _logger.LogInformation("MEDIA_IMAGE_URL_DOWNLOAD_START url={Url}", uri);

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(12);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/webp"));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Khong tai duoc media tu URL. HTTP {(int)response.StatusCode}.");
        }

        var contentType = NormalizeMimeType(response.Content.Headers.ContentType?.MediaType, uri.AbsolutePath);
        if (!IsAllowedImageMime(contentType))
        {
            throw new InvalidOperationException($"URL khong tra ve anh hop le. Content-Type: {response.Content.Headers.ContentType?.MediaType ?? "unknown"}.");
        }

        var length = response.Content.Headers.ContentLength;
        if (length.HasValue && length.Value > GetMaxImageBytes())
        {
            throw new InvalidOperationException("Media tu URL vuot qua 10MB.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            ms.Write(buffer, 0, read);
            if (ms.Length > GetMaxImageBytes())
            {
                throw new InvalidOperationException("Media tu URL vuot qua 10MB.");
            }
        }

        if (ms.Length == 0)
        {
            throw new InvalidOperationException("URL media tra ve tep rong.");
        }

        var fileName = Path.GetFileName(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName) || !Path.HasExtension(fileName))
        {
            fileName = $"product-url{ContentTypeToExtension(contentType)}";
        }

        return (ms.ToArray(), contentType, fileName, uri);
    }

    private async Task<MediaFileDto> DownloadBinaryToObjectKeyAsync(
        string fileUrl,
        string objectKey,
        string fileCategory,
        string expectedMimeType,
        Guid? userId,
        Guid? customerId,
        Guid tenantId,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("URL media dau vao phai la dia chi http/https hop le.");
        }

        _logger.LogInformation("MEDIA_BINARY_URL_DOWNLOAD_START initialHost={InitialHost} initialPathShape={InitialPathShape}",
            uri.Host, DescribePathShape(uri));

        var client = _httpClientFactory.CreateClient("MediaBinaryDownload");
        client.Timeout = TimeSpan.FromSeconds(60);
        var currentUri = uri;
        var requestUri = fileUrl;
        HttpResponseMessage? response = null;

        try
        {
            for (var hop = 0; hop < 5; hop++)
            {
                response?.Dispose();
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (response.StatusCode is not (HttpStatusCode.Moved
                    or HttpStatusCode.Redirect
                    or HttpStatusCode.RedirectMethod
                    or HttpStatusCode.RedirectKeepVerb
                    or HttpStatusCode.PermanentRedirect))
                {
                    break;
                }

                var location = response.Headers.Location;
                if (location is null)
                {
                    break;
                }

                currentUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                requestUri = currentUri.ToString();
            }

            if (response is null)
            {
                throw new InvalidOperationException("Khong tai duoc file media tu URL. HTTP 0.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var location = response.Headers.Location;
                _logger.LogWarning(
                    "MEDIA_BINARY_URL_DOWNLOAD_FAILED httpStatus={HttpStatus} initialHost={InitialHost} finalHost={FinalHost} finalPathShape={FinalPathShape} locationHost={LocationHost} locationPathShape={LocationPathShape} contentType={ContentType}",
                    (int)response.StatusCode,
                    uri.Host,
                    currentUri.Host,
                    DescribePathShape(currentUri),
                    location?.Host ?? string.Empty,
                    location is null ? string.Empty : DescribePathShape(location.IsAbsoluteUri ? location : new Uri(currentUri, location)),
                    response.Content.Headers.ContentType?.MediaType ?? "unknown");
                throw new InvalidOperationException($"Khong tai duoc file media tu URL. HTTP {(int)response.StatusCode}.");
            }

            var responseMime = NormalizeMimeType(response.Content.Headers.ContentType?.MediaType, currentUri.AbsolutePath);
            var length = response.Content.Headers.ContentLength;
            if (length.HasValue && length.Value > GetMaxVideoBytes())
            {
                throw new InvalidOperationException("File media tu URL vuot qua 10MB.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var fileName = Path.GetFileName(currentUri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName) || !Path.HasExtension(fileName))
            {
                fileName = $"media-url{ContentTypeToExtension(responseMime == "application/octet-stream" ? expectedMimeType : responseMime)}";
            }

            return await SaveDownloadedBinaryStreamAsync(stream, objectKey, fileName, fileCategory, expectedMimeType, responseMime,
                userId, customerId, tenantId, ct);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private async Task<MediaFileDto> SaveDownloadedBinaryStreamAsync(
        Stream source,
        string objectKey,
        string originalFileName,
        string fileCategory,
        string expectedMimeType,
        string responseMime,
        Guid? userId,
        Guid? customerId,
        Guid tenantId,
        CancellationToken ct)
    {
        objectKey = NormalizeObjectKey(objectKey);
        var mimeType = ResolveDownloadedBinaryMime(expectedMimeType, responseMime, originalFileName);
        if (!IsPersistableMime(mimeType))
        {
            throw new InvalidOperationException($"URL khÃ´ng tráº£ vá» media há»£p lá»‡. Content-Type: {responseMime}.");
        }

        var uploadRoot = _config["Storage:LocalUploadRoot"] ?? "wwwroot/uploads";
        var absRoot = Path.GetFullPath(Path.Combine(_env.ContentRootPath, uploadRoot));
        var absPath = Path.GetFullPath(Path.Combine(absRoot, objectKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!absPath.StartsWith(absRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage key khÃ´ng há»£p lá»‡.");
        }

        var absDir = Path.GetDirectoryName(absPath) ?? absRoot;
        Directory.CreateDirectory(absDir);
        var tempPath = Path.Combine(absDir, $".{Guid.NewGuid():N}.tmp");
        var publicBase = _config["Storage:PublicUploadBase"] ?? "/uploads";
        var publicUrl = $"{publicBase.TrimEnd('/')}/{objectKey}";
        var safeName = Path.GetFileName(absPath);
        var ext = ContentTypeToExtension(mimeType);
        var storageProvider = NormalizeDbText(_config["Storage:Provider"] ?? "local", "local");
        var id = Guid.NewGuid();
        long totalBytes = 0;
        var sniff = new byte[64];
        var sniffCount = 0;

        try
        {
            await using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    if (sniffCount < sniff.Length)
                    {
                        var copy = Math.Min(read, sniff.Length - sniffCount);
                        Array.Copy(buffer, 0, sniff, sniffCount, copy);
                        sniffCount += copy;
                    }

                    totalBytes += read;
                    if (totalBytes > GetMaxVideoBytes())
                    {
                        throw new InvalidOperationException("File media tá»« URL vÆ°á»£t quÃ¡ 10MB.");
                    }

                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }

            if (totalBytes == 0)
            {
                throw new InvalidOperationException("URL media tráº£ vá» tá»‡p rá»—ng.");
            }

            var payload = sniff.AsSpan(0, sniffCount);
            if (string.Equals(mimeType, "video/mp4", StringComparison.OrdinalIgnoreCase)
                && !LooksLikeMp4(payload))
            {
                throw new InvalidOperationException("URL media khÃ´ng tráº£ vá» video MP4 há»£p lá»‡.");
            }
            if (IsAudioMime(mimeType) && !LooksLikeAudio(payload, mimeType))
            {
                throw new InvalidOperationException("URL media khÃ´ng tráº£ vá» audio há»£p lá»‡.");
            }

            if (File.Exists(absPath))
            {
                var existing = await GetByObjectKeyAsync(tenantId, objectKey, ct);
                if (existing is not null)
                {
                    EnsureExistingMediaMatches(existing, mimeType, fileCategory);
                    return existing;
                }

                throw new InvalidOperationException("RVIDEO_MEDIA_FILE_WITHOUT_DB_RECORD: physical media appeared without a tenant media row.");
            }

            File.Move(tempPath, absPath);

            using var conn = await _factory.OpenAsync(ct);
            await conn.ExecuteAsync(
                """
                INSERT INTO media.media_files
                    (id, tenant_id, customer_id, user_id, file_category, file_name, file_ext, mime_type,
                     file_size_bytes, storage_provider, object_key, file_url, public_url, is_active, created_at, created_by)
                VALUES
                    (@id, @tenant, @customer, @user, @cat, @name, @ext, @mime,
                     @size, @storage, @key, @url, @url, true, now(), @user);
                """,
                new
                {
                    id,
                    tenant = tenantId,
                    customer = customerId,
                    user = userId,
                    cat = NormalizeDbText(fileCategory, "media"),
                    name = safeName,
                    ext,
                    mime = mimeType,
                    size = totalBytes,
                    storage = storageProvider,
                    key = objectKey,
                    url = publicUrl
                });
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }

        return new MediaFileDto
        {
            Id = id,
            UserId = userId,
            CustomerId = customerId,
            FileCategory = NormalizeDbText(fileCategory, "media"),
            FileName = safeName,
            MimeType = mimeType,
            FileSizeBytes = totalBytes,
            StorageProvider = storageProvider,
            ObjectKey = objectKey,
            FileUrl = publicUrl,
            PublicUrl = publicUrl,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static string NormalizeMimeType(string? mimeType, string originalFileName)
    {
        var normalized = (mimeType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        if (normalized is "image/jpg" or "image/pjpeg") return "image/jpeg";
        if (normalized == "audio/mp3") return "audio/mpeg";
        if (AllowedMime.Contains(normalized)) return normalized;

        return Path.GetExtension(originalFileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".mp4" => "video/mp4",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            _ => normalized
        };
    }

    private static string ContentTypeToExtension(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        "video/mp4" => ".mp4",
        "audio/mpeg" or "audio/mp3" => ".mp3",
        "audio/wav" or "audio/x-wav" => ".wav",
        "audio/mp4" or "audio/m4a" => ".m4a",
        _ => ".img"
    };

    private long GetMaxImageBytes()
        => _config.GetValue("MediaStorage:MaxImageBytes", 20L * 1024 * 1024);

    private long GetMaxVideoBytes()
        => _config.GetValue("MediaStorage:MaxVideoBytes", 500L * 1024 * 1024);

    private long GetMaxAudioBytes()
        => _config.GetValue("MediaStorage:MaxAudioBytes", 50L * 1024 * 1024);

    private long GetMaxBytesForMime(string mimeType)
        => string.Equals(mimeType, "video/mp4", StringComparison.OrdinalIgnoreCase)
            ? GetMaxVideoBytes()
            : IsAudioMime(mimeType)
                ? GetMaxAudioBytes()
            : GetMaxImageBytes();

    private static bool IsAllowedImageMime(string mimeType)
        => mimeType is "image/png" or "image/jpeg" or "image/webp";

    private static bool IsPersistableMime(string mimeType)
        => mimeType is "image/png" or "image/jpeg" or "image/webp" or "video/mp4" or "audio/mpeg" or "audio/wav" or "audio/x-wav" or "audio/mp4" or "audio/m4a";

    private static bool IsAudioMime(string mimeType)
        => mimeType is "audio/mpeg" or "audio/mp3" or "audio/wav" or "audio/x-wav" or "audio/mp4" or "audio/m4a";

    private static string DescribePathShape(Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? "/" : "/" + string.Join("/", segments.Select(_ => "{segment}"));
    }

    private static void EnsureExistingMediaMatches(MediaFileDto existing, string mimeType, string fileCategory)
    {
        if (!existing.IsActive
            || !string.Equals(existing.MimeType, mimeType, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.FileCategory, fileCategory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("RVIDEO_MEDIA_OBJECT_KEY_METADATA_MISMATCH: existing tenant media metadata does not match the expected immutable media.");
        }
    }

    private async Task<MediaFileDto?> TryGetExistingImmutableMediaAsync(
        string objectKey,
        Guid tenantId,
        string mimeType,
        string fileCategory,
        CancellationToken ct)
    {
        if (!File.Exists(ResolveLocalPath(objectKey)))
        {
            return null;
        }

        var existing = await GetByObjectKeyAsync(tenantId, objectKey, ct);
        if (existing is null)
        {
            throw new InvalidOperationException("RVIDEO_MEDIA_FILE_WITHOUT_DB_RECORD: physical media exists without a tenant media row.");
        }

        EnsureExistingMediaMatches(existing, mimeType, fileCategory);
        return existing;
    }

    private static string ResolveDownloadedBinaryMime(string expectedMimeType, string responseMime, string originalFileName)
    {
        if (string.Equals(responseMime, expectedMimeType, StringComparison.OrdinalIgnoreCase))
        {
            return responseMime;
        }

        if (string.Equals(responseMime, "application/octet-stream", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(expectedMimeType, "video/mp4", StringComparison.OrdinalIgnoreCase)
                || IsAudioMime(expectedMimeType)))
        {
            return NormalizeMimeType(expectedMimeType, originalFileName);
        }

        return NormalizeMimeType(responseMime, originalFileName);
    }

    private static bool LooksLikeMp4(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12)
        {
            return false;
        }

        for (var i = 4; i <= bytes.Length - 8; i++)
        {
            if (bytes.Slice(i, 4).SequenceEqual("ftyp"u8))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeAudio(byte[] bytes, string mimeType)
        => LooksLikeAudio(bytes.AsSpan(), mimeType);

    private static bool LooksLikeAudio(ReadOnlySpan<byte> bytes, string mimeType)
    {
        if (mimeType is "audio/mpeg" or "audio/mp3")
        {
            return LooksLikeMp3(bytes);
        }

        if (mimeType is "audio/wav" or "audio/x-wav")
        {
            return LooksLikeWav(bytes);
        }

        if (mimeType is "audio/mp4" or "audio/m4a")
        {
            return LooksLikeMp4Audio(bytes);
        }

        return false;
    }

    private static bool LooksLikeWav(ReadOnlySpan<byte> bytes)
        => bytes.Length >= 12
           && bytes[..4].SequenceEqual("RIFF"u8)
           && bytes.Slice(8, 4).SequenceEqual("WAVE"u8);

    private static bool LooksLikeMp3(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[..3].SequenceEqual("ID3"u8))
        {
            return true;
        }

        for (var i = 0; i <= bytes.Length - 4; i++)
        {
            var header = BinaryPrimitives.ReadUInt32BigEndian(bytes[i..]);
            if ((header & 0xFFE00000) != 0xFFE00000
                || ((header >> 17) & 0b11) == 0
                || ((header >> 12) & 0b1111) is 0 or 15
                || ((header >> 10) & 0b11) == 3)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool LooksLikeMp4Audio(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return false;
        }

        for (var offset = 8; offset + 4 <= Math.Min(bytes.Length, 64); offset += 4)
        {
            var brand = bytes.Slice(offset, 4);
            if (brand.SequenceEqual("isom"u8)
                || brand.SequenceEqual("iso2"u8)
                || brand.SequenceEqual("mp41"u8)
                || brand.SequenceEqual("mp42"u8)
                || brand.SequenceEqual("m4a "u8)
                || brand.SequenceEqual("M4A "u8)
                || brand.SequenceEqual("M4B "u8)
                || brand.SequenceEqual("qt  "u8))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeDbText(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return text.Length <= 50 ? text : text[..50];
    }

    private static string NormalizeObjectKey(string objectKey)
    {
        var normalized = objectKey.Trim().Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException("Storage key khÃ´ng há»£p lá»‡.");
        }

        return normalized;
    }

    private string ResolveLocalPath(string objectKey)
    {
        var uploadRoot = _config["Storage:LocalUploadRoot"] ?? "wwwroot/uploads";
        var absRoot = Path.GetFullPath(Path.Combine(_env.ContentRootPath, uploadRoot));
        var absPath = Path.GetFullPath(Path.Combine(absRoot, NormalizeObjectKey(objectKey).Replace('/', Path.DirectorySeparatorChar)));
        if (!absPath.StartsWith(absRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage key không hợp lệ.");
        }

        return absPath;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only; the caller still receives the original persistence error.
        }
    }

    private static Uri ValidatePublicImageUri(string imageUrl)
    {
        if (!Uri.TryCreate(imageUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("URL media dau ra phai la dia chi http/https hop le.");
        }

        if (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Khong cho phep tai anh tu localhost.");
        }

        var addresses = Dns.GetHostAddresses(uri.Host);
        if (addresses.Length == 0 || addresses.Any(IsPrivateAddress))
        {
            throw new InvalidOperationException("Khong cho phep tai anh tu IP noi bo/private.");
        }

        return uri;
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
        }

        return true;
    }

    private static ImageMetadata ReadImageMetadata(byte[] bytes, string mimeType)
    {
        if (bytes.Length >= 33 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            var colorType = bytes[25];
            return new ImageMetadata(
                (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)),
                (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)),
                colorType is 4 or 6);
        }

        if (bytes.Length >= 12
            && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return ReadWebpMetadata(bytes);
        }

        if (bytes.Length > 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return ReadJpegMetadata(bytes);
        }

        return new ImageMetadata(null, null, mimeType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ? false : null);
    }

    private static ImageMetadata ReadWebpMetadata(byte[] bytes)
    {
        var span = bytes.AsSpan();
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var chunk = span.Slice(offset, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset + 4, 4));
            var dataOffset = offset + 8;
            if (size < 0 || dataOffset + size > bytes.Length) break;

            if (chunk.SequenceEqual("VP8X"u8) && size >= 10)
            {
                var flags = span[dataOffset];
                var width = 1 + ReadUInt24LittleEndian(span.Slice(dataOffset + 4, 3));
                var height = 1 + ReadUInt24LittleEndian(span.Slice(dataOffset + 7, 3));
                return new ImageMetadata(width, height, (flags & 0x10) != 0);
            }
            if (chunk.SequenceEqual("VP8 "u8) && size >= 10)
            {
                var width = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(dataOffset + 6, 2)) & 0x3FFF;
                var height = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(dataOffset + 8, 2)) & 0x3FFF;
                return new ImageMetadata(width, height, false);
            }
            if (chunk.SequenceEqual("VP8L"u8) && size >= 5)
            {
                var b1 = span[dataOffset + 1];
                var b2 = span[dataOffset + 2];
                var b3 = span[dataOffset + 3];
                var b4 = span[dataOffset + 4];
                var width = 1 + (((b2 & 0x3F) << 8) | b1);
                var height = 1 + (((b4 & 0x0F) << 10) | (b3 << 2) | ((b2 & 0xC0) >> 6));
                return new ImageMetadata(width, height, true);
            }

            offset = dataOffset + size + (size % 2);
        }

        return new ImageMetadata(null, null, null);
    }

    private static ImageMetadata ReadJpegMetadata(byte[] bytes)
    {
        var offset = 2;
        while (offset + 9 < bytes.Length)
        {
            if (bytes[offset] != 0xFF) break;
            var marker = bytes[offset + 1];
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 2, 2));
            if (length < 2 || offset + 2 + length > bytes.Length) break;
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 5, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 7, 2));
                return new ImageMetadata(width, height, false);
            }
            offset += 2 + length;
        }

        return new ImageMetadata(null, null, false);
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes)
        => bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);

    private sealed record ImageMetadata(int? Width, int? Height, bool? HasAlpha);
}
