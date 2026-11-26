using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.PromptAssistant;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Services.EditVideoTool;

public sealed record EditVideoAgentScriptRequest(Guid? Service, string? Brief, int? Seconds, string? Aspect);

public sealed record EditVideoAgentServiceRow(Guid Id, string Name, string? Description);

public sealed record EditVideoAgentScene(int Index, string? Purpose, int Seconds, string ImagePrompt, string MotionPrompt, string Voice);

public sealed record EditVideoAgentScript(Guid GenerationId, string? Title, string? Cta, int TotalSeconds, IReadOnlyList<EditVideoAgentScene> Scenes);

/// <summary>
/// The dashboard's 79AI script agents (Prompt Assistant, one per service) for the EditVideo app: the app sends a
/// product brief and gets scenes back (image prompt, motion prompt, narration, length). Nothing is charged here;
/// the images, videos and voice made from the script are paid steps of their own.
/// </summary>
public sealed class EditVideoAgentService
{
    public const int MinSeconds = 10;
    public const int MaxSeconds = 120;
    public const int MaxBriefChars = 4000;

    private readonly TodoXConnectionFactory _factory;
    private readonly IServicePromptAssistantService _assistant;
    private readonly ITodoXVideoPromptParser _parser;
    private readonly ILogger<EditVideoAgentService> _logger;

    public EditVideoAgentService(TodoXConnectionFactory factory, IServicePromptAssistantService assistant, ITodoXVideoPromptParser parser,
        ILogger<EditVideoAgentService> logger)
    {
        _factory = factory;
        _assistant = assistant;
        _parser = parser;
        _logger = logger;
    }

    /// <summary>Active services whose script agent is switched on and connected to a 79AI agent.</summary>
    public async Task<IReadOnlyList<EditVideoAgentServiceRow>> ListAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<EditVideoAgentServiceRow>(new CommandDefinition(
            """
            SELECT s.id AS Id, s.service_name AS Name, COALESCE(NULLIF(s.short_description, ''), s.description) AS Description
              FROM catalog.services s
              JOIN settings.service_prompt_assistants a ON a.service_id = s.id
             WHERE lower(s.status) = 'active' AND a.enabled AND COALESCE(a.gommo_agent_id_base, '') <> ''
             ORDER BY s.sort_order, s.service_name;
            """, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<EditVideoResult<EditVideoAgentScript>> ScriptAsync(EditVideoLicenseRow license, EditVideoAgentScriptRequest body, CancellationToken ct = default)
    {
        if (Validate(body) is { } invalid)
        {
            return EditVideoResult<EditVideoAgentScript>.Fail(EditVideoErrorCodes.InvalidRequest, invalid);
        }

        var service = (await ListAsync(ct)).FirstOrDefault(s => s.Id == body.Service);
        if (service is null)
        {
            return EditVideoResult<EditVideoAgentScript>.Fail("not_found", "Kiểu video này chưa có agent viết kịch bản. Chọn kiểu khác trong danh sách.");
        }

        var brief = body.Brief!.Trim();
        if (!string.IsNullOrWhiteSpace(body.Aspect))
        {
            brief += $"\nKhung hình video: {body.Aspect.Trim()}.";
        }

        var input = PromptAssistantEndpoints.BuildAgentInput(new PromptAssistantGenerateRequest(service.Id, brief, Duration: body.Seconds, CreativeMode: true));
        ServicePromptGenerationResult result;
        try
        {
            result = await _assistant.GeneratePromptAsync(service.Id, input, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "EDITVIDEO_AGENT_FAILED license={License} service={Service}", license.Id, service.Id);
            return EditVideoResult<EditVideoAgentScript>.Fail("ai_failed", "Agent chưa viết được kịch bản lúc này. Thử lại sau ít phút.");
        }

        if (string.IsNullOrWhiteSpace(result.GeneratedJson))
        {
            _logger.LogWarning("EDITVIDEO_AGENT_EMPTY license={License} service={Service} code={Code}", license.Id, service.Id, result.ErrorCode);
            return EditVideoResult<EditVideoAgentScript>.Fail("ai_failed", "Agent chưa viết được kịch bản lúc này. Thử lại hoặc viết rõ thông tin sản phẩm hơn.",
                new { reason = result.ErrorCode, provider = result.ErrorMessage is { Length: > 200 } m ? m[..200] : result.ErrorMessage, generationId = result.GenerationId });
        }

        var script = ToScript(result.GenerationId, PromptAssistantVideoOutputNormalizer.Normalize(result.GeneratedJson), _parser);
        if (script is null)
        {
            _logger.LogWarning("EDITVIDEO_AGENT_NO_SCENES license={License} service={Service} generation={Generation}", license.Id, service.Id, result.GenerationId);
            return EditVideoResult<EditVideoAgentScript>.Fail("ai_failed", "Kịch bản agent trả về chưa có cảnh dùng được. Bấm viết lại.");
        }

        return EditVideoResult<EditVideoAgentScript>.Success(script);
    }

    internal static string? Validate(EditVideoAgentScriptRequest body)
    {
        if (body.Service is null || body.Service == Guid.Empty)
        {
            return "Chọn kiểu video.";
        }

        if (string.IsNullOrWhiteSpace(body.Brief))
        {
            return "Nhập thông tin sản phẩm.";
        }

        if (body.Brief.Length > MaxBriefChars)
        {
            return $"Thông tin sản phẩm dài quá {MaxBriefChars} ký tự.";
        }

        return body.Seconds is null or < MinSeconds or > MaxSeconds ? $"Độ dài video từ {MinSeconds} đến {MaxSeconds} giây." : null;
    }

    /// <summary>The scenes the app can make: each needs an image prompt and narration; a missing motion prompt gets a gentle default.</summary>
    internal static EditVideoAgentScript? ToScript(Guid generationId, string json, ITodoXVideoPromptParser parser)
    {
        var parsed = parser.Parse(json);
        var scenes = new List<EditVideoAgentScene>();
        foreach (var s in parsed.Model.Scenes)
        {
            var image = Clean(s.ImagePrompt) ?? Clean(s.ImagePromptFallback);
            var voice = Clean(s.TtsText) ?? Clean(s.VoiceText) ?? Clean(s.Voice);
            if (image is null || voice is null)
            {
                continue;
            }

            var seconds = Math.Clamp(s.DurationSeconds ?? PromptAssistantVideoOutputValidator.MinSceneSeconds,
                PromptAssistantVideoOutputValidator.MinSceneSeconds, PromptAssistantVideoOutputValidator.MaxSceneSeconds);
            var motion = Clean(s.MotionPrompt) ?? Clean(s.VideoPrompt) ?? "Gentle slow camera push-in, soft natural light, the product stays exactly as in the image.";
            scenes.Add(new EditVideoAgentScene(scenes.Count + 1, Clean(s.ScenePurpose), seconds, image, motion, voice));
        }

        return scenes.Count == 0
            ? null
            : new EditVideoAgentScript(generationId, Clean(parsed.Model.VideoTitle), Clean(parsed.Model.Cta), scenes.Sum(s => s.Seconds), scenes);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
