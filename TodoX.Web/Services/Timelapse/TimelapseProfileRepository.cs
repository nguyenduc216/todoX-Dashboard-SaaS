using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models.Timelapse;

namespace TodoX.Web.Services.Timelapse;

public interface ITimelapseProfileRepository
{
    Task<IReadOnlyList<TimelapseProfileDto>> GetEnabledProfilesAsync(CancellationToken ct = default);
    Task<TimelapseProfileDto?> GetEnabledProfileAsync(string profileCode, CancellationToken ct = default);
    Task<TimelapseRenderProfileDto?> GetRenderProfileAsync(string profileCode, CancellationToken ct = default);
}

/// <summary>
/// Reads the same prompt profile records consumed by the Timelapse n8n workflow.
/// Profile JSON remains in the automation database and is intentionally not copied into the dashboard.
/// </summary>
public sealed class TimelapseProfileRepository : ITimelapseProfileRepository
{
    private readonly TodoXAutomationConnectionFactory _factory;

    public TimelapseProfileRepository(TodoXAutomationConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<TimelapseProfileDto>> GetEnabledProfilesAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<TimelapseProfileDto>(
            """
            SELECT profile_code AS ProfileCode,
                   profile_name AS ProfileName,
                   enabled AS Enabled
              FROM public.todox_timelapse_prompt_profiles
             WHERE enabled = true
             ORDER BY profile_name, profile_code;
            """);
        return rows.ToList();
    }

    public async Task<TimelapseProfileDto?> GetEnabledProfileAsync(string profileCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(profileCode))
        {
            return null;
        }

        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<TimelapseProfileDto>(
            """
            SELECT profile_code AS ProfileCode,
                   profile_name AS ProfileName,
                   enabled AS Enabled
              FROM public.todox_timelapse_prompt_profiles
             WHERE profile_code = @profileCode
               AND enabled = true
             LIMIT 1;
            """,
            new { profileCode = profileCode.Trim() });
    }

    public async Task<TimelapseRenderProfileDto?> GetRenderProfileAsync(string profileCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(profileCode))
        {
            return null;
        }

        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<TimelapseRenderProfileDto>(
            """
            SELECT profile_code AS ProfileCode,
                   profile_name AS ProfileName,
                   enabled AS Enabled,
                   to_jsonb(p)::text AS ProfileJson
              FROM public.todox_timelapse_prompt_profiles p
             WHERE profile_code = @profileCode
               AND enabled = true
             LIMIT 1;
            """,
            new { profileCode = profileCode.Trim() });
    }
}
