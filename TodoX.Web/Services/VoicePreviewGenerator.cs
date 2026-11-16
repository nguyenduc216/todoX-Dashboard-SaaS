using TodoX.Web.Models;
using TodoX.Web.Services.EditVideoTool;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Services;

/// <summary>
/// "Tạo giọng mẫu" (Dashboard › AI Studio › Giọng đọc): Vbee reads one fixed sentence in a voice, the audio is saved
/// as that voice's preview file. Done once per click by an admin (about 1.000đ of Vbee per voice, paid by TodoX);
/// afterwards everyone listens to the stored file for free.
/// </summary>
public sealed class VoicePreviewGenerator
{
    public const string SampleText =
        "Xin chào, tôi là giọng đọc của TodoX. Tôi có thể đọc lời giới thiệu sản phẩm, quảng cáo và câu chuyện cho video của bạn.";

    private readonly IAiStudioCatalogService _catalog;
    private readonly IVbeeVoiceClient _vbee;
    private readonly IVbeeRuntimeConfigProvider _config;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<VoicePreviewGenerator> _logger;

    public VoicePreviewGenerator(IAiStudioCatalogService catalog, IVbeeVoiceClient vbee, IVbeeRuntimeConfigProvider config,
        IHttpClientFactory http, IConfiguration configuration, ILogger<VoicePreviewGenerator> logger)
    {
        _catalog = catalog;
        _vbee = vbee;
        _config = config;
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Makes and stores the preview; throws InvalidOperationException with a Vietnamese reason on failure.</summary>
    public async Task<AiStudioVoiceDto> GenerateAsync(AiStudioVoiceDto voice, CurrentUserSession user, CancellationToken ct = default)
    {
        if (!AdminEndpointAuthorization.IsAdmin(user))
        {
            throw new InvalidOperationException("Chỉ quản trị viên mới tạo được giọng mẫu.");
        }

        if (!string.Equals(voice.ProviderCode, "vbee", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Chỉ tạo giọng mẫu tự động cho giọng Vbee.");
        }

        var options = await _config.GetAsync(ct);
        options.GetTokenOrThrow();
        var voiceId = string.IsNullOrWhiteSpace(voice.ProviderVoiceId) ? voice.Code : voice.ProviderVoiceId.Trim();
        var publicBase = (_configuration["TodoX:PublicBaseUrl"] ?? "https://dashboard.todox.vn").TrimEnd('/');
        // Vbee needs a callback; this one points at nothing (the result is read by polling below).
        var callback = VbeeOptions.BuildAuthorizedCallbackUriOrNull($"{publicBase}/api/editvideo/v1/vbee-callback?job={Guid.NewGuid()}", options.CallbackSecret)?.ToString()
                       ?? $"{publicBase}/api/editvideo/v1/vbee-callback";

        VbeeVoiceSubmitResult submitted;
        try
        {
            submitted = await _vbee.SubmitAsync(new VbeeVoiceSubmitRequest(
                voiceId, SampleText, voice.DefaultRate, null, callback, null,
                options.ResolveSampleRate(voiceId), options.DefaultBitrate, options.DefaultSpeedRate, options.AppId), options, ct);
        }
        catch (VbeeVoiceSubmitException ex)
        {
            throw new InvalidOperationException($"Vbee từ chối giọng \"{voiceId}\": {ex.ErrorMessage ?? ex.Message}. Kiểm tra lại mã giọng (Provider voice id).");
        }

        var audioUrl = submitted.AudioUrl;
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (string.IsNullOrWhiteSpace(audioUrl) && !string.IsNullOrWhiteSpace(submitted.RequestId) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            var status = await _vbee.GetStatusAsync(submitted.RequestId!, options, ct);
            audioUrl = EditVideoVoiceService.FindString(status, "audio_link", "audio_url", "audioUrl", "download_url");
            var state = EditVideoVoiceService.FindString(status, "status", "state")?.ToUpperInvariant();
            if (state is "FAILURE" or "FAILED" or "ERROR")
            {
                throw new InvalidOperationException("Vbee không đọc được giọng này: " +
                    (EditVideoVoiceService.FindString(status, "error_message", "message") ?? "lỗi không rõ"));
            }
        }

        if (string.IsNullOrWhiteSpace(audioUrl))
        {
            throw new InvalidOperationException("Vbee chưa trả file sau 90 giây. Thử lại sau ít phút.");
        }

        var bytes = await _http.CreateClient().GetByteArrayAsync(audioUrl, ct);
        _logger.LogInformation("VOICE_PREVIEW_GENERATED voice={Code} bytes={Bytes}", voice.Code, bytes.Length);
        return await _catalog.UploadVoicePreviewAsync(voice.Id, bytes, $"preview-{voice.Code}.mp3", "audio/mpeg", user, ct);
    }
}
