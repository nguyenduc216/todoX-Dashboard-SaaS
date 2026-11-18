using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.EditVideoTool;

/// <summary>One license whose paid EditVideo commands run without the price confirmation window.</summary>
public sealed record EditVideoConsentSkip(Guid LicenseId, string Label, string EnabledBy, DateTimeOffset EnabledAt);

/// <summary>A license the admin can pick on the settings page.</summary>
public sealed record EditVideoLicenseChoice(Guid Id, string KeyPrefix, string? CustomerName, string Status);

/// <summary>
/// Dashboard › Cài đặt EditVideo › "Bỏ qua xác nhận giá": meant only for TodoX's own license. An admin adds a
/// license after typing their own dashboard password again; every paid command it runs is still logged
/// (editvideo.license_events "ai_price_skip"). Kept in system.app_settings (key editvideo.consent_skip, JSON).
/// </summary>
public sealed class EditVideoConsentSkipService
{
    public const string SettingKey = "editvideo.consent_skip";
    private const string CacheKey = "editvideo-consent-skip";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly IMemoryCache _cache;
    private readonly AccountRepository? _accounts;
    private readonly PasswordHasher? _passwords;
    private readonly ILogger<EditVideoConsentSkipService>? _logger;

    public EditVideoConsentSkipService(TodoXConnectionFactory factory, TenantContext tenant, IMemoryCache cache,
        AccountRepository? accounts = null, PasswordHasher? passwords = null, ILogger<EditVideoConsentSkipService>? logger = null)
    {
        _factory = factory;
        _tenant = tenant;
        _cache = cache;
        _accounts = accounts;
        _passwords = passwords;
        _logger = logger;
    }

    public async Task<IReadOnlyList<EditVideoConsentSkip>> ListAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyList<EditVideoConsentSkip>? cached) && cached is not null)
        {
            return cached;
        }

        using var conn = await _factory.OpenAsync(ct);
        var raw = await conn.ExecuteScalarAsync<string?>(
            "SELECT setting_value FROM system.app_settings WHERE setting_key = @key AND is_active;", new { key = SettingKey });
        var list = Parse(raw);
        _cache.Set(CacheKey, list, TimeSpan.FromSeconds(30));
        return list;
    }

    public async Task<bool> IsSkippedAsync(Guid licenseId, CancellationToken ct = default) =>
        (await ListAsync(ct)).Any(s => s.LicenseId == licenseId);

    internal static IReadOnlyList<EditVideoConsentSkip> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<EditVideoConsentSkip>>(raw, Json) ?? [];
        }
        catch (JsonException)
        {
            return []; // a damaged value never skips anything
        }
    }

    public async Task<IReadOnlyList<EditVideoLicenseChoice>> LicensesAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<EditVideoLicenseChoice>(
            """
            SELECT l.id AS Id, l.key_prefix AS KeyPrefix, c.company_name AS CustomerName, l.status AS Status
              FROM editvideo.licenses l
              LEFT JOIN crm.customers c ON c.id = l.customer_id
             WHERE l.tenant_id = @tenantId
             ORDER BY c.company_name, l.created_at;
            """, new { tenantId = _tenant.TenantId });
        return rows.ToList();
    }

    /// <summary>Adds a license; the admin must type their own dashboard password again.</summary>
    public async Task<(bool Ok, string Message)> AddAsync(EditVideoLicenseChoice license, CurrentUserSession? user, string password, CancellationToken ct = default)
    {
        if (user is null || !AdminEndpointAuthorization.IsAdmin(user))
        {
            return (false, "Chỉ quản trị viên mới bật được quyền này.");
        }

        if (user.ImpersonatorUserId is not null)
        {
            return (false, "Không bật được khi đang đăng nhập thay người khác.");
        }

        if (_accounts is null || _passwords is null || string.IsNullOrEmpty(password)
            || !_passwords.Verify(password, await _accounts.GetPasswordHashAsync(user.UserId)))
        {
            _logger?.LogWarning("EDITVIDEO_CONSENT_SKIP_PASSWORD_WRONG user={User} license={License}", user.UserId, license.Id);
            return (false, "Mật khẩu không đúng. Chưa bật.");
        }

        var list = (await ListAsync(ct)).Where(s => s.LicenseId != license.Id).ToList();
        list.Add(new EditVideoConsentSkip(license.Id, $"{license.CustomerName ?? "?"} · {license.KeyPrefix}",
            string.IsNullOrWhiteSpace(user.Email) ? user.DisplayName : user.Email, DateTimeOffset.UtcNow));
        await SaveAsync(list, ct);
        _logger?.LogWarning("EDITVIDEO_CONSENT_SKIP_ON user={User} license={License}", user.UserId, license.Id);
        return (true, "Đã bật: license này tạo nội dung tốn điểm không cần cửa sổ xác nhận (vẫn ghi log từng lần).");
    }

    public async Task RemoveAsync(Guid licenseId, CurrentUserSession? user, CancellationToken ct = default)
    {
        if (user is null || !AdminEndpointAuthorization.IsAdmin(user))
        {
            return;
        }

        await SaveAsync((await ListAsync(ct)).Where(s => s.LicenseId != licenseId).ToList(), ct);
        _logger?.LogWarning("EDITVIDEO_CONSENT_SKIP_OFF user={User} license={License}", user.UserId, licenseId);
    }

    private async Task SaveAsync(IReadOnlyList<EditVideoConsentSkip> list, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO system.app_settings (id, setting_key, setting_group, setting_type, setting_value, description, is_active, created_at)
            VALUES (gen_random_uuid(), @key, 'editvideo', 'text', @value, 'License EditVideo bỏ qua xác nhận giá (chỉ TodoX)', true, now())
            ON CONFLICT (setting_key) DO UPDATE SET setting_value = EXCLUDED.setting_value, is_active = true, updated_at = now();
            """, new { key = SettingKey, value = JsonSerializer.Serialize(list, Json) });
        _cache.Remove(CacheKey);
    }
}
