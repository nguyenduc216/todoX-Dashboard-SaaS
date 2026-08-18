using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

public sealed class RVideoJobSettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public RVideoJobSettingsRepository(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
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
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleAsync<RVideoJobSettingsDto>(
            """
            INSERT INTO video_render.rvideo_job_settings
                (project_id, tenant_id, execution_mode, current_stage, skip_character, character_mode,
                 selected_character_id, character_snapshot_json, voice_mode, voice_catalog_code,
                 voice_snapshot_json, default_tts_rate, music_catalog_code, music_snapshot_json, music_volume,
                 created_at, updated_at)
            VALUES
                (@projectId, @tenant, @executionMode, 'INFO', @skipCharacter, @characterMode,
                 @selectedCharacterId, CAST(@characterSnapshot AS jsonb), @voiceMode, @voiceCatalogCode,
                 CAST(@voiceSnapshot AS jsonb), @defaultTtsRate, @musicCatalogCode, CAST(@musicSnapshot AS jsonb), @musicVolume,
                 now(), now())
            ON CONFLICT (project_id) DO UPDATE SET
                execution_mode=EXCLUDED.execution_mode,
                skip_character=EXCLUDED.skip_character,
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
                updated_at=now();
            """ + SelectSql + " WHERE project_id=@projectId AND tenant_id=@tenant;",
            new
            {
                projectId,
                tenant = _tenant.TenantId,
                request.ExecutionMode,
                request.SkipCharacter,
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
               skip_character AS SkipCharacter, character_mode AS CharacterMode, selected_character_id AS SelectedCharacterId,
               character_snapshot_json::text AS CharacterSnapshotJson, voice_mode AS VoiceMode,
               voice_catalog_code AS VoiceCatalogCode, voice_snapshot_json::text AS VoiceSnapshotJson,
               default_tts_rate AS DefaultTtsRate, music_catalog_code AS MusicCatalogCode,
               music_snapshot_json::text AS MusicSnapshotJson, music_volume AS MusicVolume,
               created_at AS CreatedAt, updated_at AS UpdatedAt
          FROM video_render.rvideo_job_settings
        """;
}
