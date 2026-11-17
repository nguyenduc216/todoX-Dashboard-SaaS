using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using TodoX.Web.Data;
using TodoX.Web.Services.EditVideoTool;

namespace TodoX.Web.Services.Pricing;

/// <summary>What to price: a model and the choices that change its 79AI cost.</summary>
public sealed record ModelPriceQuery(string Model, string? Mode = null, string? Resolution = null, int? Seconds = null);

/// <summary>
/// The model price book (Dashboard › Bảng giá model), stored as JSON in system.app_settings ("pricing.model_book"),
/// plus 79AI's live price lists. Every AI price on the web and in EditVideo comes from here; callers snapshot the
/// result on the job, so a change only affects jobs created afterwards.
/// </summary>
public sealed class ModelPriceService
{
    public const string SettingKey = "pricing.model_book";
    private const string BookCacheKey = "model-price-book";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly TodoXConnectionFactory _factory;
    private readonly IMemoryCache _cache;
    private readonly IEditVideoGommoClient? _gommo;
    private readonly ILogger<ModelPriceService>? _logger;

    public ModelPriceService(TodoXConnectionFactory factory, IMemoryCache cache, IEditVideoGommoClient? gommo = null, ILogger<ModelPriceService>? logger = null)
    {
        _factory = factory;
        _cache = cache;
        _gommo = gommo;
        _logger = logger;
    }

    public async Task<ModelPriceBook> GetAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(BookCacheKey, out ModelPriceBook? cached) && cached is not null)
        {
            return cached;
        }

        string? stored = null;
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            stored = await conn.ExecuteScalarAsync<string?>(
                "SELECT setting_value FROM system.app_settings WHERE setting_key = @key AND is_active LIMIT 1;", new { key = SettingKey });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Model price book could not be read; using defaults.");
        }

        var book = Parse(stored);
        _cache.Set(BookCacheKey, book, TimeSpan.FromSeconds(30));
        return book;
    }

    /// <summary>Stored book, with any default model it does not know yet appended (switched on, after the others).</summary>
    public static ModelPriceBook Parse(string? json)
    {
        ModelPriceBook? book = null;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                book = JsonSerializer.Deserialize<ModelPriceBook>(json, Json);
            }
            catch (JsonException)
            {
                book = null;
            }
        }

        book ??= new ModelPriceBook();
        var models = book.Models.Where(m => !string.IsNullOrWhiteSpace(m.Model))
            .Select(m => m with { Kind = m.Kind.Trim().ToLowerInvariant(), Model = m.Model.Trim(), Env = ModelPriceEnvs.Normalize(m.Env) })
            .GroupBy(m => (m.Kind, m.Model.ToLowerInvariant())).Select(g => g.First()).ToList();
        foreach (var d in ModelPriceDefaults.Models)
        {
            if (!models.Any(m => m.Kind == d.Kind && string.Equals(m.Model, d.Model, StringComparison.OrdinalIgnoreCase)))
            {
                models.Add(d);
            }
        }

        return book with { Models = models };
    }

    public async Task SaveAsync(ModelPriceBook book, CancellationToken ct = default)
    {
        var clean = Parse(JsonSerializer.Serialize(book, Json));
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO system.app_settings (id, setting_key, setting_group, setting_type, setting_value, description, is_active, created_at)
            VALUES (gen_random_uuid(), @key, 'pricing', 'json', @value, 'Bảng giá model AI (Dashboard + EditVideo)', true, now())
            ON CONFLICT (setting_key) DO UPDATE SET setting_value = EXCLUDED.setting_value, is_active = true, updated_at = now();
            """, new { key = SettingKey, value = JsonSerializer.Serialize(clean, Json) });
        _cache.Remove(BookCacheKey);
    }

    /// <summary>79AI's model list for a kind (video, image, music, voice = TTS). Last good copy is kept if 79AI is down.</summary>
    public async Task<JsonNode?> CatalogAsync(string kind, CancellationToken ct = default)
    {
        var key = $"model-price-catalog:{kind}";
        if (_cache.TryGetValue(key, out JsonNode? cached) && cached is not null)
        {
            return cached;
        }

        if (_gommo is null)
        {
            return null;
        }

        try
        {
            var fresh = kind switch
            {
                ModelPriceKinds.Music => await _gommo.PostV2Async("/ai/models", new Dictionary<string, string?> { ["type"] = "music" }, ct),
                ModelPriceKinds.Voice => await _gommo.PostV2Async("/ai/models", new Dictionary<string, string?> { ["type"] = "tts" }, ct),
                _ => await _gommo.PostAsync("/models", new Dictionary<string, string?> { ["type"] = kind }, ct)
            };
            _cache.Set(key, fresh, TimeSpan.FromMinutes(10));
            _cache.Set(key + ":last", fresh);
            return fresh;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "79AI {Kind} price list unavailable.", kind);
            return _cache.TryGetValue(key + ":last", out JsonNode? last) ? last : null;
        }
    }

    public static JsonObject? FindModel(JsonNode? catalog, string model) =>
        EditVideoGommoJson.Models(catalog).FirstOrDefault(m =>
            string.Equals(EditVideoGommoJson.Str(m, "model"), model, StringComparison.OrdinalIgnoreCase)
            || string.Equals(EditVideoGommoJson.Str(m, "id_base"), model, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// TodoX's price for one generation in an environment, or null when the model is unknown to 79AI / has no price
    /// (callers then keep their previous rate). For voice, Seconds is the number of characters (79AI TTS prices per
    /// character); Vbee has a fixed cost per voice.
    /// </summary>
    public async Task<ModelPriceQuote?> QuoteAsync(string kind, ModelPriceQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Model))
        {
            return null;
        }

        var book = await GetAsync(ct);
        var entry = book.Find(kind, query.Model);
        decimal? cost = entry?.FixedCostVnd is > 0 ? entry.FixedCostVnd : null;
        if (cost is null)
        {
            var model = FindModel(await CatalogAsync(kind, ct), query.Model.Trim());
            var credits = model is null ? null
                : kind == ModelPriceKinds.Voice
                    ? (ModelCostLookup.Num(model, "price") is > 0 and var perChar ? perChar * Math.Max(1, query.Seconds ?? 1000) : null)
                    : ModelCostLookup.Credits(model, query.Mode, query.Resolution, kind == ModelPriceKinds.Video ? query.Seconds : null);
            cost = credits is > 0 ? book.CostVnd(credits.Value) : null;
        }

        if (cost is not > 0)
        {
            return null;
        }

        var markup = book.MarkupFor(entry);
        return new ModelPriceQuote(kind, query.Model.Trim(), entry, cost.Value, markup, book.Points(cost.Value, markup),
            kind == ModelPriceKinds.Video ? query.Seconds : null);
    }
}
