using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services;

public sealed class MrTodoXAvatarOptions
{
    public const string DefaultPromptDescription =
        "Mr. todoX is the official TodoX mascot/avatar: a friendly futuristic AI assistant, modern SaaS consultant style, professional, trustworthy, energetic, with TodoX brand colors using dark navy, white and gold/yellow accent. The character should appear naturally in the service thumbnail as a guide or presenter.";

    public string? AvatarUrl { get; set; }
    public string PromptDescription { get; set; } = DefaultPromptDescription;
}

public sealed class MrTodoXAvatarService
{
    public const string AvatarUrlKey = "todox.avatar.mr_todox_url";
    public const string PromptKey = "todox.avatar.mr_todox_prompt";

    private readonly TodoXConnectionFactory _factory;
    private readonly IConfiguration _configuration;

    public MrTodoXAvatarService(TodoXConnectionFactory factory, IConfiguration configuration)
    {
        _factory = factory;
        _configuration = configuration;
    }

    public async Task<MrTodoXAvatarOptions> GetAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<SettingRow>(
            """
            SELECT setting_key AS SettingKey, setting_value AS SettingValue
              FROM system.app_settings
             WHERE setting_key = ANY(@keys) AND is_active;
            """, new { keys = new[] { AvatarUrlKey, PromptKey } });
        var map = rows.ToDictionary(x => x.SettingKey, x => x.SettingValue, StringComparer.OrdinalIgnoreCase);

        var avatarUrl = map.GetValueOrDefault(AvatarUrlKey)
            ?? _configuration[AvatarUrlKey]
            ?? _configuration["TodoX:Avatar:MrTodoXUrl"];
        var prompt = map.GetValueOrDefault(PromptKey)
            ?? _configuration[PromptKey]
            ?? _configuration["TodoX:Avatar:MrTodoXPrompt"]
            ?? MrTodoXAvatarOptions.DefaultPromptDescription;

        return new MrTodoXAvatarOptions
        {
            AvatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl.Trim(),
            PromptDescription = string.IsNullOrWhiteSpace(prompt) ? MrTodoXAvatarOptions.DefaultPromptDescription : prompt.Trim()
        };
    }

    public Task SaveAvatarUrlAsync(string? avatarUrl, CancellationToken ct = default)
        => SaveAsync(AvatarUrlKey, avatarUrl?.Trim() ?? string.Empty, "URL hình ảnh đại diện Mr. todoX dùng làm ảnh tham chiếu khi render thumbnail dịch vụ.", ct);

    public Task SavePromptAsync(string prompt, CancellationToken ct = default)
        => SaveAsync(PromptKey,
            string.IsNullOrWhiteSpace(prompt) ? MrTodoXAvatarOptions.DefaultPromptDescription : prompt.Trim(),
            "Mô tả fallback của nhân vật Mr. todoX khi render ảnh không có reference image.", ct);

    public async Task EnsureDefaultsAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO system.app_settings (id, setting_key, setting_group, setting_type, setting_value, description, is_active, created_at)
            VALUES
                (gen_random_uuid(), @urlKey, 'branding', 'string', '', @urlDesc, true, now()),
                (gen_random_uuid(), @promptKey, 'branding', 'text', @prompt, @promptDesc, true, now())
            ON CONFLICT (setting_key) DO NOTHING;
            """,
            new
            {
                urlKey = AvatarUrlKey,
                promptKey = PromptKey,
                prompt = MrTodoXAvatarOptions.DefaultPromptDescription,
                urlDesc = "URL hình ảnh đại diện Mr. todoX dùng làm ảnh tham chiếu khi render thumbnail dịch vụ.",
                promptDesc = "Mô tả fallback của nhân vật Mr. todoX khi render ảnh không có reference image."
            });
    }

    private async Task SaveAsync(string key, string value, string description, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO system.app_settings (id, setting_key, setting_group, setting_type, setting_value, description, is_active, created_at)
            VALUES (gen_random_uuid(), @key, 'branding', 'string', @value, @description, true, now())
            ON CONFLICT (setting_key)
            DO UPDATE SET setting_value = EXCLUDED.setting_value,
                          description = EXCLUDED.description,
                          is_active = true,
                          updated_at = now();
            """,
            new { key, value, description });
    }

    private sealed class SettingRow
    {
        public string SettingKey { get; set; } = string.Empty;
        public string? SettingValue { get; set; }
    }
}
