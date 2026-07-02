using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.Media;

namespace TodoX.Web.Services.Profile;

public sealed class AvatarInfo
{
    public string? AvatarUrl { get; set; }
    public Guid? MediaId { get; set; }
    public string? AvatarType { get; set; }
}

public interface IAvatarService
{
    Task<AvatarInfo> GetCurrentAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Upload raw bytes as the user's avatar; sets it active and updates app_users.avatar_url.</summary>
    Task<AvatarInfo> UploadAsync(Guid userId, byte[] content, string fileName, string mimeType, CancellationToken ct = default);

    /// <summary>Make an existing media file (e.g. a selected chibi) the active avatar.</summary>
    Task<AvatarInfo> SetActiveFromMediaAsync(Guid userId, Guid mediaId, string avatarType, CancellationToken ct = default);
}

/// <summary>Manages auth.user_avatars + auth.app_users.avatar_url per the Sprint 2F contract.</summary>
public sealed class AvatarService : IAvatarService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly IMediaFileService _media;
    private readonly TenantContext _tenant;

    public AvatarService(TodoXConnectionFactory factory, IMediaFileService media, TenantContext tenant)
    {
        _factory = factory;
        _media = media;
        _tenant = tenant;
    }

    public async Task<AvatarInfo> GetCurrentAsync(Guid userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<AvatarInfo>(
            """
            SELECT u.avatar_url AS AvatarUrl, a.source_media_id AS MediaId, a.avatar_type AS AvatarType
              FROM auth.app_users u
              LEFT JOIN auth.user_avatars a ON a.user_id = u.id AND a.is_active = true
             WHERE u.id = @uid
             LIMIT 1;
            """, new { uid = userId });
        return row ?? new AvatarInfo();
    }

    public async Task<AvatarInfo> UploadAsync(Guid userId, byte[] content, string fileName, string mimeType, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        var media = await _media.SaveAsync(content, fileName, mimeType, "avatar", userId, null, _tenant.TenantId, ct);
        return await SetActiveFromMediaAsync(userId, media.Id, "uploaded", ct);
    }

    public async Task<AvatarInfo> SetActiveFromMediaAsync(Guid userId, Guid mediaId, string avatarType, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);

        // Deactivate previous active avatars.
        await conn.ExecuteAsync(
            "UPDATE auth.user_avatars SET is_active=false WHERE user_id=@uid AND is_active=true;",
            new { uid = userId });

        await conn.ExecuteAsync(
            """
            INSERT INTO auth.user_avatars (id, tenant_id, user_id, avatar_type, source_media_id, is_active, is_default, created_at, created_by)
            VALUES (gen_random_uuid(), @tenant, @uid, @type, @media, true, true, now(), @uid);
            """,
            new { tenant = _tenant.TenantId, uid = userId, type = avatarType, media = mediaId });

        var mediaRow = await _media.GetAsync(mediaId, ct);
        await conn.ExecuteAsync(
            "UPDATE auth.app_users SET avatar_url=@url, updated_at=now() WHERE id=@uid;",
            new { url = mediaRow?.PublicUrl, uid = userId });

        return new AvatarInfo { AvatarUrl = mediaRow?.PublicUrl, MediaId = mediaId, AvatarType = avatarType };
    }
}
