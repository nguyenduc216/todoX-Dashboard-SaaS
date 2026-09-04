using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services;

/// <summary>
/// Reads/writes point pricing + wallet defaults from system.app_settings, with sane fallbacks.
/// Legacy setting keys still use token.* to avoid a disruptive DB rename in Phase 1.
/// </summary>
public sealed class TokenSettingsService
{
    public const string KeyChibiImage = "token.cost.chibi_image";
    public const string KeyGeminiPrompt = "token.cost.gemini_prompt";
    public const string KeyDefaultBalance = "token.default_wallet_balance";
    public const string KeyChargeStaticImagePoints = "todox.billing.charge_static_image_points";

    private readonly TodoXConnectionFactory _factory;

    public TokenSettingsService(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public Task<decimal> GetChibiImageCostAsync() => GetDecimalAsync(KeyChibiImage, 10m);
    public Task<decimal> GetGeminiPromptCostAsync() => GetDecimalAsync(KeyGeminiPrompt, 1m);
    public Task<decimal> GetDefaultWalletBalanceAsync() => GetDecimalAsync(KeyDefaultBalance, 100m);
    public Task<bool> GetChargeStaticImagePointsAsync() => GetBoolAsync(KeyChargeStaticImagePoints, true);

    public async Task<decimal> GetDecimalAsync(string key, decimal fallback)
    {
        using var conn = await _factory.OpenAsync();
        var val = await conn.ExecuteScalarAsync<string?>(
            "SELECT setting_value FROM system.app_settings WHERE setting_key=@k AND is_active;", new { k = key });
        return decimal.TryParse(val, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : fallback;
    }

    public async Task SetAsync(string key, string value, string group = "billing", string? description = null)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO system.app_settings (id, setting_key, setting_group, setting_type, setting_value, description, is_active, created_at)
            VALUES (gen_random_uuid(), @key, @group, 'number', @value, @desc, true, now())
            ON CONFLICT (setting_key) DO UPDATE SET setting_value = EXCLUDED.setting_value, updated_at = now();
            """, new { key, group, value, desc = description });
    }

    public async Task SetBoolAsync(string key, bool value, string group = "billing", string? description = null)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO system.app_settings (id, setting_key, setting_group, setting_type, setting_value, description, is_active, created_at)
            VALUES (gen_random_uuid(), @key, @group, 'boolean', @value, @desc, true, now())
            ON CONFLICT (setting_key) DO UPDATE SET setting_value = EXCLUDED.setting_value, setting_type = EXCLUDED.setting_type, updated_at = now();
            """, new { key, group, value = value ? "true" : "false", desc = description });
    }

    /// <summary>Seed default pricing keys if missing (idempotent).</summary>
    public async Task EnsureDefaultsAsync()
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO system.app_settings (id, setting_key, setting_group, setting_type, setting_value, description, is_active, created_at)
            VALUES
                (gen_random_uuid(), @k1, 'billing', 'number', '10',  'Điểm trừ cho mỗi ảnh chibi', true, now()),
                (gen_random_uuid(), @k2, 'billing', 'number', '1',   'Điểm trừ cho mỗi lần Gemini sinh prompt', true, now()),
                (gen_random_uuid(), @k3, 'billing', 'number', '100', 'Số dư điểm mặc định cho tài khoản khách hàng', true, now()),
                (gen_random_uuid(), @k4, 'billing', 'boolean', 'true', 'Trừ điểm ảnh tĩnh cho RDance và Timelapse', true, now())
            ON CONFLICT (setting_key) DO NOTHING;
            """, new { k1 = KeyChibiImage, k2 = KeyGeminiPrompt, k3 = KeyDefaultBalance, k4 = KeyChargeStaticImagePoints });
    }

    private async Task<bool> GetBoolAsync(string key, bool fallback)
    {
        using var conn = await _factory.OpenAsync();
        var val = await conn.ExecuteScalarAsync<string?>(
            "SELECT setting_value FROM system.app_settings WHERE setting_key=@k AND is_active;", new { k = key });
        return bool.TryParse(val, out var b) ? b : fallback;
    }
}
