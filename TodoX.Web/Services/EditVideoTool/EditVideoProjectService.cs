using Dapper;
using Microsoft.Extensions.Options;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services.SharedMedia;

namespace TodoX.Web.Services.EditVideoTool;

/// <summary>One exported video as the app reports it (the file itself is uploaded only on "Đồng bộ").</summary>
public sealed class EditVideoSyncVideo
{
    public string? ClientId { get; init; }
    public string? Title { get; init; }
    public string? FileName { get; init; }
    public decimal? DurationSeconds { get; init; }
    public string? Aspect { get; init; }
    public string? Style { get; init; }
    public string? Skill { get; init; }
    public int? SkillVersion { get; init; }
    public long? SizeBytes { get; init; }
    public DateTimeOffset? RenderedAt { get; init; }
}

public sealed class EditVideoSyncProject
{
    public string? Name { get; init; }
    public List<EditVideoSyncVideo>? Videos { get; init; }
}

public sealed class EditVideoSyncRequest
{
    public List<EditVideoSyncProject>? Projects { get; init; }
}

/// <summary>What the dashboard knows about a video: the app keeps it to show "đã đồng bộ" or ask to sync.</summary>
public sealed record EditVideoSyncedVideo(string ClientId, Guid Id, string Status, string? VideoUrl);

public sealed record EditVideoProjectRow(Guid Id, string ProjectName, DateTime CreatedAt, DateTime UpdatedAt, int Videos, int SyncedVideos, decimal Points);

public sealed record EditVideoProjectVideoRow(Guid Id, string Title, string FileName, decimal? DurationSeconds, string? Aspect, string? Style,
    string? Skill, int? SkillVersion, long? SizeBytes, DateTime? RenderedAt, string SyncStatus, string? VideoUrl, DateTime? UploadedAt);

public sealed record EditVideoProjectTaskRow(Guid Id, string Kind, string Model, string Status, decimal Points, bool Charged, DateTime CreatedAt);

public sealed record EditVideoProjectDetail(EditVideoProjectRow Project, IReadOnlyList<EditVideoProjectVideoRow> Videos, IReadOnlyList<EditVideoProjectTaskRow> Tasks);

/// <summary>
/// The customer's EditVideo projects on the dashboard ("Jobs của tôi"). The app reports projects and exported videos
/// by itself; a video file is uploaded only when the person presses "Đồng bộ" and is stored with the other job videos
/// (customer-videos/videos), outside the EditVideo storage quota.
/// </summary>
public sealed class EditVideoProjectService
{
    public const long MaxChunkBytes = 9L * 1024 * 1024;

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly SharedMediaPathService _paths;
    private readonly SharedMediaOptions _media;

    public EditVideoProjectService(TodoXConnectionFactory factory, TenantContext tenant, SharedMediaPathService paths, IOptions<SharedMediaOptions> media)
    {
        _factory = factory;
        _tenant = tenant;
        _paths = paths;
        _media = media.Value;
    }

    private static string Cut(string? value, int max) => (value ?? "").Trim() is var v && v.Length > max ? v[..max] : (value ?? "").Trim();

    /// <summary>Upserts projects and video information; returns each video's dashboard id and sync state.</summary>
    public async Task<EditVideoResult<IReadOnlyList<EditVideoSyncedVideo>>> SyncAsync(EditVideoLicenseRow license, EditVideoDeviceRow device,
        EditVideoSyncRequest request, CancellationToken ct = default)
    {
        var projects = (request.Projects ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Name)).Take(200).ToList();
        if (projects.Sum(p => p.Videos?.Count ?? 0) > 2000)
        {
            return EditVideoResult<IReadOnlyList<EditVideoSyncedVideo>>.Fail(EditVideoErrorCodes.InvalidRequest, "Quá nhiều video trong một lần đồng bộ.");
        }

        await _tenant.EnsureLoadedAsync(ct);
        var result = new List<EditVideoSyncedVideo>();
        using var conn = await _factory.OpenAsync(ct);
        foreach (var p in projects)
        {
            var name = Cut(p.Name, 120);
            var key = EditVideoStorageService.ProjectKey(name);
            if (key.Length == 0)
            {
                continue;
            }

            var projectId = await conn.ExecuteScalarAsync<Guid>(
                """
                INSERT INTO editvideo.projects (tenant_id, customer_id, license_id, project_key, project_name)
                VALUES (@tenantId, @customerId, @licenseId, @key, @name)
                ON CONFLICT (tenant_id, customer_id, project_key)
                DO UPDATE SET project_name = EXCLUDED.project_name, license_id = EXCLUDED.license_id, updated_at = now()
                RETURNING id;
                """, new { tenantId = _tenant.TenantId, customerId = license.CustomerId, licenseId = license.Id, key, name });

            foreach (var v in (p.Videos ?? []).Where(v => !string.IsNullOrWhiteSpace(v.ClientId)))
            {
                var row = await conn.QuerySingleAsync<(Guid Id, string SyncStatus, string? VideoUrl)>(
                    """
                    INSERT INTO editvideo.project_videos (tenant_id, customer_id, project_id, license_id, device_id, client_video_id, title, file_name,
                        duration_seconds, aspect, style, skill, skill_version, size_bytes, rendered_at)
                    VALUES (@tenantId, @customerId, @projectId, @licenseId, @deviceId, @clientId, @title, @fileName,
                        @duration, @aspect, @style, @skill, @skillVersion, @size, @renderedAt)
                    ON CONFLICT (tenant_id, customer_id, client_video_id) DO UPDATE SET
                        project_id = EXCLUDED.project_id, title = EXCLUDED.title, file_name = EXCLUDED.file_name,
                        duration_seconds = EXCLUDED.duration_seconds, aspect = EXCLUDED.aspect, style = EXCLUDED.style,
                        skill = EXCLUDED.skill, skill_version = EXCLUDED.skill_version, size_bytes = EXCLUDED.size_bytes,
                        rendered_at = EXCLUDED.rendered_at, updated_at = now()
                    RETURNING id AS Id, sync_status AS SyncStatus, video_url AS VideoUrl;
                    """,
                    new
                    {
                        tenantId = _tenant.TenantId, customerId = license.CustomerId, projectId, licenseId = license.Id, deviceId = device.Id,
                        clientId = Cut(v.ClientId, 120), title = Cut(string.IsNullOrWhiteSpace(v.Title) ? v.FileName : v.Title, 200),
                        fileName = Cut(v.FileName, 200), duration = v.DurationSeconds, aspect = Cut(v.Aspect, 10), style = Cut(v.Style, 80),
                        skill = Cut(v.Skill, 80), skillVersion = v.SkillVersion, size = v.SizeBytes, renderedAt = v.RenderedAt
                    });
                result.Add(new EditVideoSyncedVideo(v.ClientId!.Trim(), row.Id, row.SyncStatus, row.VideoUrl));
            }
        }

        return EditVideoResult<IReadOnlyList<EditVideoSyncedVideo>>.Success(result);
    }

    /// <summary>One chunk of a video being synced ("Đồng bộ"); the last chunk publishes it with the other job videos.</summary>
    public async Task<EditVideoResult<EditVideoSyncedVideo?>> AppendVideoAsync(EditVideoLicenseRow license, Guid videoId, long totalSize, long offset,
        Stream body, CancellationToken ct = default)
    {
        var maxBytes = _media.VideoSampleMaxVideoBytes > 0 ? _media.VideoSampleMaxVideoBytes : 500L * 1024 * 1024;
        if (totalSize <= 0 || totalSize > maxBytes || offset < 0 || offset >= totalSize)
        {
            return Fail($"Video phải nhỏ hơn {maxBytes / 1024 / 1024} MB.");
        }

        using var conn = await _factory.OpenAsync(ct);
        var video = await conn.QuerySingleOrDefaultAsync<(Guid Id, string ClientId, string SyncStatus)>(
            "SELECT id AS Id, client_video_id AS ClientId, sync_status AS SyncStatus FROM editvideo.project_videos WHERE id = @videoId AND customer_id = @customerId;",
            new { videoId, customerId = license.CustomerId });
        if (video.Id == Guid.Empty)
        {
            return EditVideoResult<EditVideoSyncedVideo?>.Fail("not_found", "Không tìm thấy video này trên TodoX. Mở lại app để đồng bộ thông tin trước.");
        }

        var temp = _paths.GetCustomerVideoPhysicalFolder("temp");
        Directory.CreateDirectory(temp);
        var partPath = Path.Combine(temp, $"editvideo-{videoId:N}.part");
        if (offset == 0)
        {
            File.Delete(partPath);
            await conn.ExecuteAsync("UPDATE editvideo.project_videos SET sync_status = 'uploading', updated_at = now() WHERE id = @videoId AND sync_status <> 'synced';", new { videoId });
        }
        else if (!File.Exists(partPath) || new FileInfo(partPath).Length != offset)
        {
            return Fail("Lượt tải lên bị gián đoạn. Bấm Đồng bộ lại.");
        }

        long written = 0;
        await using (var file = new FileStream(partPath, FileMode.Append, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(buffer, ct)) > 0)
            {
                written += read;
                if (written > MaxChunkBytes || offset + written > totalSize)
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
            return EditVideoResult<EditVideoSyncedVideo?>.Success(null);
        }

        if (!LooksLikeMp4(partPath))
        {
            File.Delete(partPath);
            return Fail("File không phải video MP4.");
        }

        var fileName = $"{Guid.NewGuid():N}.mp4";
        var videos = _paths.GetCustomerVideoPhysicalFolder("videos");
        Directory.CreateDirectory(videos);
        File.Move(partPath, Path.Combine(videos, fileName));
        var url = _paths.GetCustomerVideoPublicUrl("videos", fileName);
        await conn.ExecuteAsync(
            "UPDATE editvideo.project_videos SET sync_status = 'synced', video_url = @url, size_bytes = @size, uploaded_at = now(), updated_at = now() WHERE id = @videoId;",
            new { videoId, url, size = totalSize });
        await conn.ExecuteAsync("UPDATE editvideo.projects SET updated_at = now() WHERE id = (SELECT project_id FROM editvideo.project_videos WHERE id = @videoId);", new { videoId });
        return EditVideoResult<EditVideoSyncedVideo?>.Success(new EditVideoSyncedVideo(video.ClientId, videoId, "synced", url));
    }

    private static bool LooksLikeMp4(string path)
    {
        using var f = File.OpenRead(path);
        var head = new byte[12];
        return f.Read(head, 0, 12) == 12 && head[4] == (byte)'f' && head[5] == (byte)'t' && head[6] == (byte)'y' && head[7] == (byte)'p';
    }

    private static EditVideoResult<EditVideoSyncedVideo?> Fail(string message) =>
        EditVideoResult<EditVideoSyncedVideo?>.Fail(EditVideoErrorCodes.InvalidRequest, message);

    private const string ProjectSelect =
        """
        SELECT p.id AS Id, p.project_name AS ProjectName, p.created_at AS CreatedAt, p.updated_at AS UpdatedAt,
               (SELECT count(*)::int FROM editvideo.project_videos v WHERE v.project_id = p.id) AS Videos,
               (SELECT count(*)::int FROM editvideo.project_videos v WHERE v.project_id = p.id AND v.sync_status = 'synced') AS SyncedVideos,
               COALESCE((SELECT sum(t.points) FROM editvideo.ai_tasks t
                          JOIN editvideo.ai_projects ap ON ap.provider_project_id = t.provider_project_id AND ap.customer_id = t.customer_id
                         WHERE ap.customer_id = p.customer_id AND ap.project_key = p.project_key AND t.charged), 0) AS Points
          FROM editvideo.projects p
        """;

    /// <summary>The customer's projects for "Jobs của tôi", newest activity first.</summary>
    public async Task<IReadOnlyList<EditVideoProjectRow>> ListOwnedAsync(CurrentUserSession user, int take, CancellationToken ct = default)
    {
        if (user is not { IsCustomer: true, CustomerId: Guid customerId })
        {
            return [];
        }

        using var conn = await _factory.OpenAsync(ct);
        return (await conn.QueryAsync<EditVideoProjectRow>(ProjectSelect + " WHERE p.customer_id = @customerId ORDER BY p.updated_at DESC LIMIT @take;",
            new { customerId, take = Math.Clamp(take, 1, 500) })).ToList();
    }

    /// <summary>A project with its videos and AI tasks, for its owner or an admin.</summary>
    public async Task<EditVideoProjectDetail?> GetAsync(Guid projectId, CurrentUserSession? user, CancellationToken ct = default)
    {
        if (user is null)
        {
            return null;
        }

        var admin = AdminEndpointAuthorization.IsAdmin(user);
        using var conn = await _factory.OpenAsync(ct);
        var project = await conn.QuerySingleOrDefaultAsync<EditVideoProjectRow>(
            ProjectSelect + " WHERE p.id = @projectId AND (@admin OR p.customer_id = @customerId);",
            new { projectId, admin, customerId = user.CustomerId ?? Guid.Empty });
        if (project is null)
        {
            return null;
        }

        var videos = (await conn.QueryAsync<EditVideoProjectVideoRow>(
            """
            SELECT id AS Id, title AS Title, file_name AS FileName, duration_seconds AS DurationSeconds, aspect AS Aspect, style AS Style,
                   skill AS Skill, skill_version AS SkillVersion, size_bytes AS SizeBytes, rendered_at AS RenderedAt,
                   sync_status AS SyncStatus, video_url AS VideoUrl, uploaded_at AS UploadedAt
              FROM editvideo.project_videos WHERE project_id = @projectId
             ORDER BY COALESCE(rendered_at, created_at) DESC;
            """, new { projectId })).ToList();
        var tasks = (await conn.QueryAsync<EditVideoProjectTaskRow>(
            """
            SELECT t.id AS Id, t.kind AS Kind, t.model AS Model, t.status AS Status, t.points AS Points, t.charged AS Charged, t.created_at AS CreatedAt
              FROM editvideo.ai_tasks t
              JOIN editvideo.ai_projects ap ON ap.provider_project_id = t.provider_project_id AND ap.customer_id = t.customer_id
              JOIN editvideo.projects p ON p.customer_id = ap.customer_id AND p.project_key = ap.project_key
             WHERE p.id = @projectId
             ORDER BY t.created_at DESC LIMIT 200;
            """, new { projectId })).ToList();
        return new EditVideoProjectDetail(project, videos, tasks);
    }
}
