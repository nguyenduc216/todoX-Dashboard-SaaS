using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.AiProviders;

namespace TodoX.Web.Services.EditVideoTool;

/// <summary>
/// Back-up script writer for "Tạo video từ sản phẩm": when the service's 79AI agent fails, Claude writes the scenes in
/// the same TodoX JSON shape (video_title, cta, scenes[scene_purpose, duration_seconds, image_prompt, motion_prompt, tts_text]).
/// The Anthropic API key is entered by an admin and kept encrypted (same AES-GCM key store as the provider credentials)
/// in system.app_settings; it is never shown again, only its last four characters.
/// </summary>
public sealed class EditVideoClaudeScriptWriter
{
    public const string Model = "claude-opus-5-5";
    private const string KeySetting = "editvideo.agent.claude_key";

    private readonly TodoXConnectionFactory _factory;
    private readonly IProviderCredentialProtector _protector;

    public EditVideoClaudeScriptWriter(TodoXConnectionFactory factory, IProviderCredentialProtector protector)
    {
        _factory = factory;
        _protector = protector;
    }

    /// <summary>"****abcd" when a key is saved, otherwise null.</summary>
    public async Task<string?> HintAsync(CancellationToken ct = default) => (await LoadAsync(ct))?.Hint ?? null;

    public async Task SaveKeyAsync(string apiKey, CancellationToken ct = default)
    {
        var key = _protector.Normalize(apiKey);
        if (!key.StartsWith("sk-ant-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Khoá Claude bắt đầu bằng sk-ant-.");
        }

        var p = await _protector.ProtectAsync(key, ct);
        var stored = JsonSerializer.Serialize(new StoredKey(Convert.ToBase64String(p.Ciphertext), Convert.ToBase64String(p.Nonce),
            Convert.ToBase64String(p.AuthTag), p.KeyVersion, p.MaskedHint));
        await WriteAsync(stored, ct);
    }

    public Task ClearKeyAsync(CancellationToken ct = default) => WriteAsync("", ct);

    /// <summary>The script as TodoX JSON, or null when no key is saved. Throws when Claude cannot be reached.</summary>
    public async Task<string?> WriteScriptAsync(string serviceName, string? serviceDescription, string brief, int seconds, string? aspect,
        CancellationToken ct = default)
    {
        if (await LoadAsync(ct) is not { } stored)
        {
            return null;
        }

        var apiKey = await _protector.UnprotectAsync(new ProviderSecureCredentialRecord
        {
            Ciphertext = Convert.FromBase64String(stored.Cipher),
            Nonce = Convert.FromBase64String(stored.Nonce),
            AuthTag = Convert.FromBase64String(stored.Tag),
            KeyVersion = stored.Version
        }, ct);

        var client = new AnthropicClient { ApiKey = apiKey };
        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 16000,
            System = SystemPrompt,
            OutputConfig = new OutputConfig { Effort = Effort.Medium, Format = new JsonOutputFormat { Schema = Schema } },
            Messages = [new UserMessageParam { Content = UserPrompt(serviceName, serviceDescription, brief, seconds, aspect) }]
        }, ct);

        if (response.StopReason == "refusal")
        {
            throw new InvalidOperationException("Claude declined to write this script.");
        }

        var json = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();
        return json.Length == 0 ? null : json;
    }

    internal static string UserPrompt(string serviceName, string? serviceDescription, string brief, int seconds, string? aspect) =>
        $"""
        Kiểu video: {serviceName}
        {(string.IsNullOrWhiteSpace(serviceDescription) ? "" : $"Mô tả kiểu video: {serviceDescription.Trim()}\n")}Tổng thời lượng: khoảng {seconds} giây.
        Khung hình: {(string.IsNullOrWhiteSpace(aspect) ? "9:16" : aspect.Trim())}.

        Thông tin sản phẩm khách nhập:
        {brief.Trim()}
        """;

    private const string SystemPrompt =
        """
        You write short Vietnamese product advertising videos for TodoX. The customer gives a product photo (used as the reference
        for every scene image) and the product details. Each scene becomes one AI image, then one AI video clip made from that image,
        and a Vietnamese voice reads the narration over it.

        Write 3 to 8 scenes whose duration_seconds (each 4 to 15) add up to the requested total. Open with a hook, show the
        product's highlights, give the price or offer if one is given, and end with a call to action using the contact if given.

        Per scene:
        - scene_purpose: short label (hook, highlight, usage, offer, cta...).
        - image_prompt: English, detailed still image for an AI image model: the product exactly as in the reference photo
          (same shape, colours, label, logo), setting, lighting, composition for the requested aspect ratio. No text overlays.
        - motion_prompt: English, how the camera and subject move during the clip. Nobody speaks and there are no subtitles;
          the product must keep its exact look.
        - tts_text: Vietnamese narration for this scene, natural spoken style, about 2.5 words per second of duration_seconds.
          Only facts the customer gave; never invent prices, discounts, certifications or health claims.
        video_title: short Vietnamese title. cta: the closing call to action in Vietnamese.
        """;

    private static readonly Dictionary<string, JsonElement> Schema = new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "video_title", "cta", "scenes" }),
        ["properties"] = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["video_title"] = new { type = "string" },
            ["cta"] = new { type = "string" },
            ["scenes"] = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "scene_purpose", "duration_seconds", "image_prompt", "motion_prompt", "tts_text" },
                    properties = new Dictionary<string, object>
                    {
                        ["scene_purpose"] = new { type = "string" },
                        ["duration_seconds"] = new { type = "integer" },
                        ["image_prompt"] = new { type = "string" },
                        ["motion_prompt"] = new { type = "string" },
                        ["tts_text"] = new { type = "string" }
                    }
                }
            }
        })
    };

    private async Task<StoredKey?> LoadAsync(CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        var value = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT setting_value FROM system.app_settings WHERE setting_key = @key AND is_active;", new { key = KeySetting }, cancellationToken: ct));
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StoredKey>(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task WriteAsync(string value, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO system.app_settings (id, setting_key, setting_group, setting_type, setting_value, description, is_active, created_at)
            VALUES (gen_random_uuid(), @key, 'editvideo', 'text', @value, 'Khoá Claude (mã hoá) viết kịch bản dự phòng khi 79AI lỗi', true, now())
            ON CONFLICT (setting_key) DO UPDATE SET setting_value = EXCLUDED.setting_value, is_active = true, updated_at = now();
            """, new { key = KeySetting, value }, cancellationToken: ct));
    }

    private sealed record StoredKey(string Cipher, string Nonce, string Tag, int Version, string? Hint);
}
