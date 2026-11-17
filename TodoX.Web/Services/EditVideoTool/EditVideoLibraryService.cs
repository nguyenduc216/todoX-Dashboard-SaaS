using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.AiCharacters;
using TodoX.Web.Services.Pricing;

namespace TodoX.Web.Services.EditVideoTool;

public sealed record EditVideoLibraryCharacter(long Id, string Name, string? Description, string? Gender, string? Style, string? ImageUrl);
public sealed record EditVideoLibraryVoice(string Code, string Name, string? Gender, string? Language, string? PreviewUrl, bool IsDefault);
public sealed record EditVideoLibraryMusic(string Code, string Name, string Source, string? Category, int? DurationSeconds, string? Url, DateTime? CreatedAt);
public sealed record EditVideoLibraryVfx(string Model, string Name, string Tier);

public sealed record EditVideoLibrary(
    IReadOnlyList<EditVideoLibraryCharacter> Characters,
    IReadOnlyList<EditVideoLibraryVoice> Voices,
    IReadOnlyList<EditVideoLibraryMusic> Music,
    IReadOnlyList<EditVideoLibraryVfx> Vfx);

/// <summary>
/// What the account already has on the dashboard, for the desktop app to show and download: the customer's AI
/// characters, TodoX voices, the music library (TodoX's background music plus the customer's own AI songs) and the
/// VFX models switched on for EditVideo. Links are absolute so the app can download them.
/// </summary>
public sealed class EditVideoLibraryService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly IAiStudioCatalogService _catalog;
    private readonly ModelPriceService? _prices;
    private readonly IConfiguration _configuration;

    public EditVideoLibraryService(TodoXConnectionFactory factory, IAiStudioCatalogService catalog, IConfiguration configuration, ModelPriceService? prices = null)
    {
        _factory = factory;
        _catalog = catalog;
        _configuration = configuration;
        _prices = prices;
    }

    private string Absolute(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return url ?? "";
        }

        var root = (_configuration["TodoX:PublicBaseUrl"] ?? "https://dashboard.todox.vn").TrimEnd('/');
        return root + "/" + url.TrimStart('/');
    }

    public async Task<EditVideoLibrary> GetAsync(EditVideoLicenseRow license, string? kind, CancellationToken ct = default)
    {
        var all = string.IsNullOrWhiteSpace(kind) || kind == "all";
        bool Want(string k) => all || string.Equals(kind, k, StringComparison.OrdinalIgnoreCase);

        IReadOnlyList<EditVideoLibraryCharacter> characters = [];
        IReadOnlyList<EditVideoLibraryVoice> voices = [];
        var music = new List<EditVideoLibraryMusic>();
        IReadOnlyList<EditVideoLibraryVfx> vfx = [];
        using var conn = await _factory.OpenAsync(ct);

        if (Want("characters"))
        {
            characters = (await conn.QueryAsync<EditVideoLibraryCharacter>(
                """
                SELECT id AS Id, character_name AS Name, description AS Description, gender AS Gender, style_preset AS Style, master_image_url AS ImageUrl
                  FROM public.todox_ai_character
                 WHERE customer_id = @scope AND lower(trim(coalesce(status, 'active'))) = 'active'
                 ORDER BY sort_order, created_at DESC;
                """, new { scope = AiCharacterService.ToBigIntCustomerId(license.CustomerId) }))
                .Select(c => c with { ImageUrl = Absolute(c.ImageUrl) }).ToList();
        }

        if (Want("voices"))
        {
            voices = (await _catalog.ListVoicesAsync(activeOnly: true, ct: ct))
                .Where(v => string.Equals(v.ProviderCode, "vbee", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => v.IsDefault).ThenBy(v => v.SortOrder)
                .Select(v => new EditVideoLibraryVoice(v.Code, v.Name, v.Gender, v.LanguageCode, Absolute(v.PreviewFileUrl), v.IsDefault)).ToList();
        }

        if (Want("music"))
        {
            music.AddRange((await _catalog.ListMusicAsync(activeOnly: true, ct: ct))
                .OrderByDescending(m => m.IsDefault).ThenBy(m => m.SortOrder)
                .Select(m => new EditVideoLibraryMusic(m.Code, m.Name, "todox", m.Category, m.DurationSeconds, Absolute(m.FileUrl), null)));
            var songs = await conn.QueryAsync<(Guid Id, string? Options, string? Outputs, DateTime CreatedAt)>(
                """
                SELECT id AS Id, options::text AS Options, outputs::text AS Outputs, created_at AS CreatedAt
                  FROM editvideo.ai_tasks
                 WHERE customer_id = @customerId AND kind = 'music' AND status = 'succeeded'
                 ORDER BY created_at DESC LIMIT 100;
                """, new { customerId = license.CustomerId });
            foreach (var s in songs)
            {
                var name = "Nhạc AI";
                try
                {
                    if (s.Options is { Length: > 0 } && JsonDocument.Parse(s.Options).RootElement.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } nm)
                    {
                        name = nm;
                    }
                }
                catch (JsonException)
                {
                }

                var urls = new List<string>();
                try
                {
                    if (s.Outputs is { Length: > 0 } && JsonDocument.Parse(s.Outputs).RootElement is { ValueKind: JsonValueKind.Array } arr)
                    {
                        urls.AddRange(arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!));
                    }
                }
                catch (JsonException)
                {
                }

                for (var i = 0; i < urls.Count; i++)
                {
                    music.Add(new EditVideoLibraryMusic($"ai-{s.Id:N}-{i + 1}", urls.Count > 1 ? $"{name} (bản {i + 1})" : name, "ai", "ai", null, urls[i], s.CreatedAt));
                }
            }
        }

        if (Want("vfx") && _prices is not null)
        {
            vfx = (await _prices.GetAsync(ct)).Ordered(ModelPriceKinds.Video, ModelPriceEnvs.EditVideo)
                .Where(e => ModelPriceBook.Group(e.Model) == "edit")
                .Select(e => new EditVideoLibraryVfx(e.Model, e.Name, e.IsPremium ? "Cao cấp" : "Thường")).ToList();
        }

        return new EditVideoLibrary(characters, voices, music, vfx);
    }
}
