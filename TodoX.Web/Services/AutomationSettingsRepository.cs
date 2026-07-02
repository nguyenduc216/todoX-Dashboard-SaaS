using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services;

public sealed record ProviderSetting(string Key, string Category, string ValueType, string? Value, string? Description)
{
    public bool IsSecret => string.Equals(ValueType, "secret", StringComparison.OrdinalIgnoreCase);
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Value);

    /// <summary>Value safe to render: secrets never expose the raw value.</summary>
    public string DisplayValue => IsSecret
        ? (IsConfigured ? "••••••••" : "(chưa cấu hình)")
        : (Value ?? string.Empty);
}

public sealed record SettingCategory(string Name, IReadOnlyList<ProviderSetting> Settings);

/// <summary>
/// Read/update access to the automation database's public.todox_settings.
/// Secret-typed values are never returned in raw form to the UI layer via DisplayValue.
/// </summary>
public sealed class AutomationSettingsRepository
{
    private readonly TodoXAutomationConnectionFactory _factory;

    public AutomationSettingsRepository(TodoXAutomationConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<SettingCategory>> GetGroupedAsync(IEnumerable<string>? categories = null)
    {
        using var conn = await _factory.OpenAsync();
        var filter = categories?.ToArray();
        var rows = await conn.QueryAsync<ProviderSetting>(
            """
            SELECT key AS Key, category AS Category, COALESCE(value_type,'string') AS ValueType,
                   value AS Value, description AS Description
              FROM public.todox_settings
             WHERE (@cats IS NULL OR category = ANY(@cats))
             ORDER BY category, key;
            """, new { cats = filter });

        return rows
            .GroupBy(r => r.Category)
            .OrderBy(g => g.Key)
            .Select(g => new SettingCategory(g.Key, g.ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync()
    {
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<string>(
            "SELECT DISTINCT category FROM public.todox_settings ORDER BY category;");
        return rows.ToList();
    }

    /// <summary>Update a single setting value. Blocks blanking a secret unintentionally is left to the caller.</summary>
    public async Task UpdateAsync(string key, string? value)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE public.todox_settings SET value=@value, updated_at=now() WHERE key=@key;",
            new { key, value });
    }
}
