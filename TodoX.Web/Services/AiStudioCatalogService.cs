using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services.Media;

namespace TodoX.Web.Services;

public interface IAiStudioCatalogService
{
    Task<IReadOnlyList<AiStudioVoiceDto>> ListVoicesAsync(AiStudioCatalogFilter? filter = null, bool activeOnly = false, CancellationToken ct = default);
    Task<AiStudioVoiceDto?> GetVoiceAsync(Guid id, CancellationToken ct = default);
    Task<AiStudioVoiceDto?> GetVoiceByCodeAsync(string code, bool activeOnly = false, CancellationToken ct = default);
    Task<AiStudioVoiceDto> SaveVoiceAsync(AiStudioVoiceDto voice, CurrentUserSession user, CancellationToken ct = default);
    Task DisableVoiceAsync(Guid id, CurrentUserSession user, CancellationToken ct = default);
    Task<AiStudioVoiceDto> UploadVoicePreviewAsync(Guid id, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default);

    Task<IReadOnlyList<AiStudioMusicDto>> ListMusicAsync(AiStudioCatalogFilter? filter = null, bool activeOnly = false, CancellationToken ct = default);
    Task<AiStudioMusicDto?> GetMusicAsync(Guid id, CancellationToken ct = default);
    Task<AiStudioMusicDto?> GetMusicByCodeAsync(string code, bool activeOnly = false, CancellationToken ct = default);
    Task<AiStudioMusicDto> SaveMusicAsync(AiStudioMusicDto music, CurrentUserSession user, CancellationToken ct = default);
    Task DisableMusicAsync(Guid id, CurrentUserSession user, CancellationToken ct = default);
    Task<AiStudioMusicDto> UploadMusicFileAsync(Guid id, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default);
}

public sealed class AiStudioCatalogService : IAiStudioCatalogService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly IMediaFileService _media;
    private readonly ILogger<AiStudioCatalogService> _logger;

    public AiStudioCatalogService(TodoXConnectionFactory factory, TenantContext tenant, IMediaFileService media, ILogger<AiStudioCatalogService> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _media = media;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AiStudioVoiceDto>> ListVoicesAsync(AiStudioCatalogFilter? filter = null, bool activeOnly = false, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var sql = VoiceSelect + """
             WHERE (@activeOnly = false OR v.is_active = true)
               AND (@isActive IS NULL OR v.is_active = @isActive)
               AND (@provider IS NULL OR lower(v.provider_code) = lower(@provider))
               AND (@gender IS NULL OR lower(COALESCE(v.gender, '')) = lower(@gender))
               AND (@language IS NULL OR lower(COALESCE(v.language_code, '')) = lower(@language))
               AND (@search IS NULL OR v.name ILIKE '%' || @search || '%' OR v.code ILIKE '%' || @search || '%' OR COALESCE(v.description, '') ILIKE '%' || @search || '%')
             ORDER BY v.sort_order, v.name, v.code;
            """;
        return (await conn.QueryAsync<AiStudioVoiceDto>(sql, FilterArgs(filter, activeOnly))).ToList();
    }

    public async Task<AiStudioVoiceDto?> GetVoiceAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AiStudioVoiceDto>(VoiceSelect + " WHERE v.id = @id LIMIT 1;", new { id });
    }

    public async Task<AiStudioVoiceDto?> GetVoiceByCodeAsync(string code, bool activeOnly = false, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AiStudioVoiceDto>(
            VoiceSelect + " WHERE lower(v.code) = lower(@code) AND (@activeOnly = false OR v.is_active = true) LIMIT 1;",
            new { code, activeOnly });
    }

    public async Task<AiStudioVoiceDto> SaveVoiceAsync(AiStudioVoiceDto voice, CurrentUserSession user, CancellationToken ct = default)
    {
        EnsureAdmin(user);
        voice.Code = AiStudioCatalogRules.NormalizeCode(voice.Code);
        voice.ProviderCode = AiStudioCatalogRules.NormalizeCode(voice.ProviderCode);
        if (voice.DefaultRate == 0) voice.DefaultRate = 1.0m;
        AiStudioCatalogRules.ValidateVoice(voice);

        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM public.ai_studio_voices WHERE lower(code) = lower(@code) AND id <> @id);",
            new { voice.Code, voice.Id }, tx);
        if (exists) throw new InvalidOperationException("VOICE_CODE_DUPLICATE");

        if (voice.IsDefault && voice.IsActive)
        {
            await conn.ExecuteAsync("UPDATE public.ai_studio_voices SET is_default=false, updated_at=now(), updated_by=@user WHERE is_default=true AND id <> @id;",
                new { id = voice.Id, user = user.UserId.ToString() }, tx);
        }

        await conn.ExecuteAsync(
            """
            INSERT INTO public.ai_studio_voices
                (id, name, code, provider_code, provider_voice_id, compatibility_alias, gender, language_code, region, description,
                 preview_file_name, preview_storage_key, preview_file_url, default_rate, min_rate, max_rate,
                 is_active, is_default, sort_order, created_at, created_by, updated_at, updated_by)
            VALUES
                (@Id, @Name, @Code, @ProviderCode, @ProviderVoiceId, @CompatibilityAlias, @Gender, @LanguageCode, @Region, @Description,
                 @PreviewFileName, @PreviewStorageKey, @PreviewFileUrl, @DefaultRate, @MinRate, @MaxRate,
                 @IsActive, @IsDefault, @SortOrder, now(), @UserId, now(), @UserId)
            ON CONFLICT (id) DO UPDATE SET
                name=EXCLUDED.name,
                code=EXCLUDED.code,
                provider_code=EXCLUDED.provider_code,
                provider_voice_id=EXCLUDED.provider_voice_id,
                compatibility_alias=EXCLUDED.compatibility_alias,
                gender=EXCLUDED.gender,
                language_code=EXCLUDED.language_code,
                region=EXCLUDED.region,
                description=EXCLUDED.description,
                default_rate=EXCLUDED.default_rate,
                min_rate=EXCLUDED.min_rate,
                max_rate=EXCLUDED.max_rate,
                is_active=EXCLUDED.is_active,
                is_default=EXCLUDED.is_default,
                sort_order=EXCLUDED.sort_order,
                updated_at=now(),
                updated_by=EXCLUDED.updated_by;
            """,
            new
            {
                voice.Id,
                voice.Name,
                voice.Code,
                voice.ProviderCode,
                voice.ProviderVoiceId,
                voice.CompatibilityAlias,
                voice.Gender,
                voice.LanguageCode,
                voice.Region,
                voice.Description,
                voice.PreviewFileName,
                voice.PreviewStorageKey,
                voice.PreviewFileUrl,
                voice.DefaultRate,
                voice.MinRate,
                voice.MaxRate,
                voice.IsActive,
                voice.IsDefault,
                voice.SortOrder,
                UserId = user.UserId.ToString()
            }, tx);
        tx.Commit();
        _logger.LogInformation("AI_STUDIO_VOICE_SAVED id={Id} code={Code} user={UserId}", voice.Id, voice.Code, user.UserId);
        return await GetVoiceAsync(voice.Id, ct) ?? voice;
    }

    public async Task DisableVoiceAsync(Guid id, CurrentUserSession user, CancellationToken ct = default)
    {
        EnsureAdmin(user);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync("UPDATE public.ai_studio_voices SET is_active=false, is_default=false, updated_at=now(), updated_by=@user WHERE id=@id;",
            new { id, user = user.UserId.ToString() });
        _logger.LogInformation("AI_STUDIO_VOICE_DISABLED id={Id} user={UserId}", id, user.UserId);
    }

    public async Task<AiStudioVoiceDto> UploadVoicePreviewAsync(Guid id, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default)
    {
        EnsureAdmin(user);
        var voice = await GetVoiceAsync(id, ct) ?? throw new InvalidOperationException("VOICE_NOT_FOUND");
        AiStudioCatalogRules.ValidateAudioUpload(fileName, contentType, content.Length, allowWaveAndM4a: false);
        var mime = AiStudioCatalogRules.NormalizeAudioMime(contentType, fileName);
        var objectKey = $"ai-studio/voices/{voice.Code}/preview-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.mp3";
        var media = await _media.SaveBinaryAtObjectKeyAsync(content, objectKey, fileName, mime, "ai_studio_voice_preview", user.UserId, null, _tenant.TenantId, ct);

        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE public.ai_studio_voices
               SET preview_file_name=@fileName,
                   preview_storage_key=@storageKey,
                   preview_file_url=@fileUrl,
                   updated_at=now(),
                   updated_by=@userId
             WHERE id=@id;
            """,
            new { id, fileName = Path.GetFileName(fileName), storageKey = media.ObjectKey, fileUrl = media.PublicUrl ?? media.FileUrl, userId = user.UserId.ToString() });
        _logger.LogInformation("AI_STUDIO_VOICE_PREVIEW_UPLOADED id={Id} objectKey={ObjectKey} user={UserId}", id, media.ObjectKey, user.UserId);
        return await GetVoiceAsync(id, ct) ?? voice;
    }

    public async Task<IReadOnlyList<AiStudioMusicDto>> ListMusicAsync(AiStudioCatalogFilter? filter = null, bool activeOnly = false, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var sql = MusicSelect + """
             WHERE (@activeOnly = false OR m.is_active = true)
               AND (@isActive IS NULL OR m.is_active = @isActive)
               AND (@category IS NULL OR lower(COALESCE(m.category, '')) = lower(@category))
               AND (@search IS NULL OR m.name ILIKE '%' || @search || '%' OR m.code ILIKE '%' || @search || '%' OR COALESCE(m.description, '') ILIKE '%' || @search || '%')
             ORDER BY m.sort_order, m.name, m.code;
            """;
        return (await conn.QueryAsync<AiStudioMusicDto>(sql, FilterArgs(filter, activeOnly))).ToList();
    }

    public async Task<AiStudioMusicDto?> GetMusicAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AiStudioMusicDto>(MusicSelect + " WHERE m.id = @id LIMIT 1;", new { id });
    }

    public async Task<AiStudioMusicDto?> GetMusicByCodeAsync(string code, bool activeOnly = false, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AiStudioMusicDto>(
            MusicSelect + " WHERE lower(m.code) = lower(@code) AND (@activeOnly = false OR m.is_active = true) LIMIT 1;",
            new { code, activeOnly });
    }

    public async Task<AiStudioMusicDto> SaveMusicAsync(AiStudioMusicDto music, CurrentUserSession user, CancellationToken ct = default)
    {
        EnsureAdmin(user);
        music.Code = AiStudioCatalogRules.NormalizeCode(music.Code);
        if (string.IsNullOrWhiteSpace(music.Category)) music.Category = "other";
        if (music.DefaultVolume == 0) music.DefaultVolume = 0.8m;
        AiStudioCatalogRules.ValidateMusic(music);

        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM public.ai_studio_music WHERE lower(code) = lower(@code) AND id <> @id);",
            new { music.Code, music.Id }, tx);
        if (exists) throw new InvalidOperationException("MUSIC_CODE_DUPLICATE");

        if (music.IsDefault && music.IsActive)
        {
            await conn.ExecuteAsync("UPDATE public.ai_studio_music SET is_default=false, updated_at=now(), updated_by=@user WHERE is_default=true AND id <> @id;",
                new { id = music.Id, user = user.UserId.ToString() }, tx);
        }

        await conn.ExecuteAsync(
            """
            INSERT INTO public.ai_studio_music
                (id, name, code, description, category, file_name, storage_key, file_url, duration_seconds, mime_type, file_size,
                 default_volume, loop_allowed, is_active, is_default, sort_order, created_at, created_by, updated_at, updated_by)
            VALUES
                (@Id, @Name, @Code, @Description, @Category, @FileName, @StorageKey, @FileUrl, @DurationSeconds, @MimeType, @FileSize,
                 @DefaultVolume, @LoopAllowed, @IsActive, @IsDefault, @SortOrder, now(), @UserId, now(), @UserId)
            ON CONFLICT (id) DO UPDATE SET
                name=EXCLUDED.name,
                code=EXCLUDED.code,
                description=EXCLUDED.description,
                category=EXCLUDED.category,
                default_volume=EXCLUDED.default_volume,
                loop_allowed=EXCLUDED.loop_allowed,
                is_active=EXCLUDED.is_active,
                is_default=EXCLUDED.is_default,
                sort_order=EXCLUDED.sort_order,
                updated_at=now(),
                updated_by=EXCLUDED.updated_by;
            """,
            new
            {
                music.Id,
                music.Name,
                music.Code,
                music.Description,
                music.Category,
                music.FileName,
                music.StorageKey,
                music.FileUrl,
                music.DurationSeconds,
                music.MimeType,
                music.FileSize,
                music.DefaultVolume,
                music.LoopAllowed,
                music.IsActive,
                music.IsDefault,
                music.SortOrder,
                UserId = user.UserId.ToString()
            }, tx);
        tx.Commit();
        _logger.LogInformation("AI_STUDIO_MUSIC_SAVED id={Id} code={Code} user={UserId}", music.Id, music.Code, user.UserId);
        return await GetMusicAsync(music.Id, ct) ?? music;
    }

    public async Task DisableMusicAsync(Guid id, CurrentUserSession user, CancellationToken ct = default)
    {
        EnsureAdmin(user);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync("UPDATE public.ai_studio_music SET is_active=false, is_default=false, updated_at=now(), updated_by=@user WHERE id=@id;",
            new { id, user = user.UserId.ToString() });
        _logger.LogInformation("AI_STUDIO_MUSIC_DISABLED id={Id} user={UserId}", id, user.UserId);
    }

    public async Task<AiStudioMusicDto> UploadMusicFileAsync(Guid id, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default)
    {
        EnsureAdmin(user);
        var music = await GetMusicAsync(id, ct) ?? throw new InvalidOperationException("MUSIC_NOT_FOUND");
        AiStudioCatalogRules.ValidateAudioUpload(fileName, contentType, content.Length);
        var mime = AiStudioCatalogRules.NormalizeAudioMime(contentType, fileName);
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var objectKey = $"ai-studio/music/{music.Code}/{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{ext}";
        var media = await _media.SaveBinaryAtObjectKeyAsync(content, objectKey, fileName, mime, "ai_studio_music", user.UserId, null, _tenant.TenantId, ct);

        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE public.ai_studio_music
               SET file_name=@fileName,
                   storage_key=@storageKey,
                   file_url=@fileUrl,
                   mime_type=@mime,
                   file_size=@size,
                   updated_at=now(),
                   updated_by=@userId
             WHERE id=@id;
            """,
            new { id, fileName = Path.GetFileName(fileName), storageKey = media.ObjectKey, fileUrl = media.PublicUrl ?? media.FileUrl, mime, size = media.FileSizeBytes, userId = user.UserId.ToString() });
        _logger.LogInformation("AI_STUDIO_MUSIC_UPLOADED id={Id} objectKey={ObjectKey} user={UserId}", id, media.ObjectKey, user.UserId);
        return await GetMusicAsync(id, ct) ?? music;
    }

    private static void EnsureAdmin(CurrentUserSession user)
    {
        if (user.IsAuthenticated != true || !(user.IsRoot || user.Role is TodoXUserRole.Admin or TodoXUserRole.SystemOperator))
        {
            throw new InvalidOperationException("AI_STUDIO_ADMIN_REQUIRED");
        }
    }

    private static object FilterArgs(AiStudioCatalogFilter? filter, bool activeOnly)
        => new
        {
            activeOnly,
            isActive = filter?.IsActive,
            provider = NullIfBlank(filter?.ProviderCode),
            gender = NullIfBlank(filter?.Gender),
            language = NullIfBlank(filter?.LanguageCode),
            category = NullIfBlank(filter?.Category),
            search = NullIfBlank(filter?.Search)
        };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private const string VoiceSelect = """
        SELECT v.id AS Id,
               v.name AS Name,
               v.code AS Code,
               v.provider_code AS ProviderCode,
               v.provider_voice_id AS ProviderVoiceId,
               v.compatibility_alias AS CompatibilityAlias,
               v.gender AS Gender,
               v.language_code AS LanguageCode,
               v.region AS Region,
               v.description AS Description,
               v.preview_file_name AS PreviewFileName,
               v.preview_storage_key AS PreviewStorageKey,
               v.preview_file_url AS PreviewFileUrl,
               v.default_rate AS DefaultRate,
               v.min_rate AS MinRate,
               v.max_rate AS MaxRate,
               v.is_active AS IsActive,
               v.is_default AS IsDefault,
               v.sort_order AS SortOrder,
               v.created_at AS CreatedAt,
               v.created_by AS CreatedBy,
               v.updated_at AS UpdatedAt,
               v.updated_by AS UpdatedBy
          FROM public.ai_studio_voices v
        """;

    private const string MusicSelect = """
        SELECT m.id AS Id,
               m.name AS Name,
               m.code AS Code,
               m.description AS Description,
               m.category AS Category,
               m.file_name AS FileName,
               m.storage_key AS StorageKey,
               m.file_url AS FileUrl,
               m.duration_seconds AS DurationSeconds,
               m.mime_type AS MimeType,
               m.file_size AS FileSize,
               m.default_volume AS DefaultVolume,
               m.loop_allowed AS LoopAllowed,
               m.is_active AS IsActive,
               m.is_default AS IsDefault,
               m.sort_order AS SortOrder,
               m.created_at AS CreatedAt,
               m.created_by AS CreatedBy,
               m.updated_at AS UpdatedAt,
               m.updated_by AS UpdatedBy
          FROM public.ai_studio_music m
        """;
}
