using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services;

namespace TodoX.Web.Services.VideoRender;

public sealed class RVideoJobSettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly IAiStudioCatalogService _catalog;

    public RVideoJobSettingsRepository(TodoXConnectionFactory factory, TenantContext tenant, IAiStudioCatalogService catalog)
    {
        _factory = factory;
        _tenant = tenant;
        _catalog = catalog;
    }

    public async Task<RVideoJobSettingsDto?> GetAsync(long projectId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<RVideoJobSettingsDto>(
            SelectSql + " WHERE project_id=@projectId AND tenant_id=@tenant;",
            new { projectId, tenant = _tenant.TenantId });
    }

    public async Task<RVideoJobSettingsDto> SaveAsync(long projectId, RVideoJobSettingsRequest request, CancellationToken ct = default)
    {
        RVideoRules.ValidateSettings(request);
        if (request.VoiceMode == RVideoVoiceModes.Library)
        {
            RVideoRules.ValidateActiveVoice(
                await _catalog.GetVoiceByCodeAsync(request.VoiceCatalogCode!, activeOnly: true, ct),
                request);
        }
        if (!string.IsNullOrWhiteSpace(request.MusicCatalogCode))
        {
            RVideoRules.ValidateActiveMusic(
                await _catalog.GetMusicByCodeAsync(request.MusicCatalogCode!, activeOnly: true, ct),
                request);
        }
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var projectTenantId = await conn.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT tenant_id FROM video_render.video_projects WHERE id=@projectId LIMIT 1;",
            new { projectId });
        RVideoRules.EnsureProjectOwnership(projectTenantId, _tenant.TenantId);

        var upsertedProjectId = await conn.QuerySingleOrDefaultAsync<long?>(
            """
            INSERT INTO video_render.rvideo_job_settings
                (project_id, tenant_id, execution_mode, current_stage, skip_character, use_reference_image_for_all_scenes, character_mode,
                 selected_character_id, character_snapshot_json, voice_mode, voice_catalog_code,
                 voice_snapshot_json, default_tts_rate, music_catalog_code, music_snapshot_json, music_volume,
                 created_at, updated_at)
            VALUES
                (@projectId, @tenant, @executionMode, 'INFO', @skipCharacter, @useReferenceImageForAllScenes, @characterMode,
                 @selectedCharacterId, CAST(@characterSnapshot AS jsonb), @voiceMode, @voiceCatalogCode,
                 CAST(@voiceSnapshot AS jsonb), @defaultTtsRate, @musicCatalogCode, CAST(@musicSnapshot AS jsonb), @musicVolume,
                 now(), now())
            ON CONFLICT (project_id) DO UPDATE SET
                execution_mode=EXCLUDED.execution_mode,
                skip_character=EXCLUDED.skip_character,
                use_reference_image_for_all_scenes=EXCLUDED.use_reference_image_for_all_scenes,
                character_mode=EXCLUDED.character_mode,
                selected_character_id=EXCLUDED.selected_character_id,
                character_snapshot_json=EXCLUDED.character_snapshot_json,
                voice_mode=EXCLUDED.voice_mode,
                voice_catalog_code=EXCLUDED.voice_catalog_code,
                voice_snapshot_json=EXCLUDED.voice_snapshot_json,
                default_tts_rate=EXCLUDED.default_tts_rate,
                music_catalog_code=EXCLUDED.music_catalog_code,
                music_snapshot_json=EXCLUDED.music_snapshot_json,
                music_volume=EXCLUDED.music_volume,
                updated_at=now()
            WHERE video_render.rvideo_job_settings.tenant_id = EXCLUDED.tenant_id
            RETURNING project_id;
            """,
            new
            {
                projectId,
                tenant = _tenant.TenantId,
                request.ExecutionMode,
                request.SkipCharacter,
                request.UseReferenceImageForAllScenes,
                request.CharacterMode,
                request.SelectedCharacterId,
                characterSnapshot = JsonSerializer.Serialize(request.CharacterSnapshot ?? new { }, JsonOptions),
                request.VoiceMode,
                request.VoiceCatalogCode,
                voiceSnapshot = JsonSerializer.Serialize(request.VoiceSnapshot ?? new { }, JsonOptions),
                request.DefaultTtsRate,
                request.MusicCatalogCode,
                musicSnapshot = JsonSerializer.Serialize(request.MusicSnapshot ?? new { }, JsonOptions),
                request.MusicVolume
            });

        if (upsertedProjectId is null)
            throw new InvalidOperationException("RVVIDEO_PROJECT_NOT_FOUND");

        return await conn.QuerySingleAsync<RVideoJobSettingsDto>(
            SelectSql + " WHERE project_id=@projectId AND tenant_id=@tenant;",
            new { projectId = upsertedProjectId.Value, tenant = _tenant.TenantId });
    }

    public async Task SetStageAsync(long projectId, string stage, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE video_render.rvideo_job_settings SET current_stage=@stage, updated_at=now() WHERE project_id=@projectId AND tenant_id=@tenant;",
            new { projectId, stage, tenant = _tenant.TenantId });
    }

    private const string SelectSql = """
        SELECT project_id AS ProjectId, execution_mode AS ExecutionMode, current_stage AS CurrentStage,
               skip_character AS SkipCharacter, use_reference_image_for_all_scenes AS UseReferenceImageForAllScenes,
               character_mode AS CharacterMode, selected_character_id AS SelectedCharacterId,
               character_snapshot_json::text AS CharacterSnapshotJson, voice_mode AS VoiceMode,
               voice_catalog_code AS VoiceCatalogCode, voice_snapshot_json::text AS VoiceSnapshotJson,
               default_tts_rate AS DefaultTtsRate, music_catalog_code AS MusicCatalogCode,
               music_snapshot_json::text AS MusicSnapshotJson, music_volume AS MusicVolume,
               created_at AS CreatedAt, updated_at AS UpdatedAt
          FROM video_render.rvideo_job_settings
        """;
}
