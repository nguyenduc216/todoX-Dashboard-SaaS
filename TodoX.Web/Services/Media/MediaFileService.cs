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

    Task<MediaFileDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Read raw bytes for a media file (used to pass reference images to the render API).</summary>
    Task<byte[]?> ReadBytesAsync(Guid id, CancellationToken ct = default);

    Task<MediaFileDto> ReplaceContentAsync(Guid mediaId, byte[] content, string mimeType,
        Guid userId, CancellationToken ct = default);

    /// <summary>Verify a media row belongs to the given user (ownership check).</summary>
    Task<bool> IsOwnedByAsync(Guid mediaId, Guid userId, CancellationToken ct = default);

    Task<ReferenceImage?> BuildReferenceImageAsync(Guid mediaId, string role, Guid userId, CancellationToken ct = default);

    Task<MediaFileDto> DownloadAndSaveImageAsync(string imageUrl, string fileCategory,
        Guid? userId, Guid? customerId, Guid tenantId, CancellationToken ct = default);
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
        var metadata = ReadImageMetadata(content, mimeType);

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

        _logger.LogInformation(
            "REFERENCE_IMAGE_STORED id={Id} category={Cat} file={FileName} mime={MimeType} size={Size} width={Width} height={Height} hasAlpha={HasAlpha} objectKey={ObjectKey} publicUrl={PublicUrl}",
            id, fileCategory, safeName, mimeType, content.Length, metadata.Width, metadata.Height, metadata.HasAlpha, objectKey, publicUrl);

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

    public async Task<MediaFileDto> ReplaceContentAsync(Guid mediaId, byte[] content, string mimeType,
        Guid userId, CancellationToken ct = default)
    {
        var media = await GetAsync(mediaId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy ảnh cần lưu.");
        if (media.UserId is Guid owner && owner != userId)
        {
            throw new InvalidOperationException("Bạn không có quyền sửa ảnh này.");
        }

        mimeType = NormalizeMimeType(mimeType, media.FileName);
        if (content.Length == 0) throw new InvalidOperationException("Tệp rỗng.");
        if (content.Length > MaxBytes) throw new InvalidOperationException("Tệp vượt quá 10MB.");
        if (!AllowedMime.Contains(mimeType)) throw new InvalidOperationException("Chỉ chấp nhận ảnh PNG, JPEG, WEBP.");
        if (string.IsNullOrWhiteSpace(media.ObjectKey))
        {
            throw new InvalidOperationException("Ảnh không có đường dẫn lưu trữ để ghi đè.");
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
        var uri = ValidatePublicImageUri(imageUrl);
        _logger.LogInformation("PRODUCT_IMAGE_URL_DOWNLOAD_START url={Url}", uri);

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(12);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/webp"));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Khong tai duoc anh san pham tu URL. HTTP {(int)response.StatusCode}.");
        }

        var contentType = NormalizeMimeType(response.Content.Headers.ContentType?.MediaType, uri.AbsolutePath);
        if (!AllowedMime.Contains(contentType))
        {
            throw new InvalidOperationException($"URL khong tra ve anh hop le. Content-Type: {response.Content.Headers.ContentType?.MediaType ?? "unknown"}.");
        }

        var length = response.Content.Headers.ContentLength;
        if (length is > MaxBytes)
        {
            throw new InvalidOperationException("Anh san pham tu URL vuot qua 10MB.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            ms.Write(buffer, 0, read);
            if (ms.Length > MaxBytes)
            {
                throw new InvalidOperationException("Anh san pham tu URL vuot qua 10MB.");
            }
        }

        if (ms.Length == 0)
        {
            throw new InvalidOperationException("URL anh san pham tra ve tep rong.");
        }

        var fileName = Path.GetFileName(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName) || !Path.HasExtension(fileName))
        {
            fileName = $"product-url{ContentTypeToExtension(contentType)}";
        }

        var saved = await SaveAsync(ms.ToArray(), fileName, contentType, fileCategory,
            userId, customerId, tenantId, ct);
        _logger.LogInformation("PRODUCT_IMAGE_URL_DOWNLOAD_SUCCESS url={Url} mediaId={MediaId} mime={MimeType} size={Size}",
            uri, saved.Id, saved.MimeType, saved.FileSizeBytes);
        return saved;
    }

    private static string NormalizeMimeType(string? mimeType, string originalFileName)
    {
        var normalized = (mimeType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        if (normalized is "image/jpg" or "image/pjpeg") return "image/jpeg";
        if (AllowedMime.Contains(normalized)) return normalized;

        return Path.GetExtension(originalFileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => normalized
        };
    }

    private static string ContentTypeToExtension(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        _ => ".img"
    };

    private static Uri ValidatePublicImageUri(string imageUrl)
    {
        if (!Uri.TryCreate(imageUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("URL anh san pham phai la http/https hop le.");
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
