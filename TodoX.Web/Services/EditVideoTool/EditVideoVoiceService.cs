using System.Net;
using System.Text.Json.Nodes;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Services.EditVideoTool;

public sealed record EditVideoVoice(string Code, string Name, string? Gender, string? Language, string? PreviewUrl, bool IsDefault);

public sealed record EditVideoVoiceJob(Guid Id, string Status, string? AudioUrl, decimal Points, string? ErrorCode, string? Message);

/// <summary>
/// Text-to-speech for the local tool through TodoX's own Vbee account (customers never hold a Vbee key).
/// Reuses the existing Vbee client, voice catalog, point price table and wallet without changing them:
/// the customer pays the configured voice rate once per succeeded job.
/// </summary>
public sealed class EditVideoVoiceService
{
    public const string QualityTier = "premium";
    public const int MaxChars = 3000;
    private const string Operation = "editvideo_voice";

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly IAiStudioCatalogService _catalog;
    private readonly IPointPricingService _pricing;
    private readonly WalletService _wallets;
    private readonly IVbeeVoiceClient _vbee;
    private readonly IVbeeRuntimeConfigProvider _vbeeConfig;
    private readonly EditVideoUsageService _usage;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EditVideoVoiceService> _logger;

    public EditVideoVoiceService(
        TodoXConnectionFactory factory,
        TenantContext tenant,
        IAiStudioCatalogService catalog,
        IPointPricingService pricing,
        WalletService wallets,
        IVbeeVoiceClient vbee,
        IVbeeRuntimeConfigProvider vbeeConfig,
        EditVideoUsageService usage,
        IConfiguration configuration,
        ILogger<EditVideoVoiceService> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _catalog = catalog;
        _pricing = pricing;
        _wallets = wallets;
        _vbee = vbee;
        _vbeeConfig = vbeeConfig;
        _usage = usage;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<(IReadOnlyList<EditVideoVoice> Voices, decimal PointsPerJob, decimal Balance)> ListAsync(Guid customerId, CancellationToken ct = default)
    {
        var voices = (await _catalog.ListVoicesAsync(activeOnly: true, ct: ct))
            .Where(v => string.Equals(v.ProviderCode, "vbee", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.IsDefault).ThenBy(v => v.SortOrder)
            .Select(v => new EditVideoVoice(v.Code, v.Name, v.Gender, v.LanguageCode, v.PreviewFileUrl, v.IsDefault))
            .ToList();
        var rate = await _pricing.ResolveRateAsync(null, PointPricingResourceTypes.Voice, QualityTier, ct);
        return (voices, rate.Rate, await _wallets.GetBalanceAsync(customerId));
    }

    public async Task<EditVideoResult<EditVideoVoiceJob>> SubmitAsync(
        EditVideoLicenseRow license, EditVideoDeviceRow device, string? text, string? voiceCode, Guid? sessionId, CancellationToken ct = default)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxChars)
        {
            return Fail("invalid_request", $"Văn bản cần từ 1 đến {MaxChars} ký tự.");
        }

        var voices = await _catalog.ListVoicesAsync(activeOnly: true, ct: ct);
        var voice = voices
            .Where(v => string.Equals(v.ProviderCode, "vbee", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => string.Equals(v.Code, voiceCode, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(v => v.IsDefault)
            .FirstOrDefault(v => string.IsNullOrWhiteSpace(voiceCode) || string.Equals(v.Code, voiceCode, StringComparison.OrdinalIgnoreCase));
        if (voice is null)
        {
            return Fail("voice_not_found", "Không tìm thấy giọng đọc này. Dùng todox_voice_list để xem danh sách.");
        }

        var rate = (await _pricing.ResolveRateAsync(null, PointPricingResourceTypes.Voice, QualityTier, ct)).Rate;
        var balance = await _wallets.GetBalanceAsync(license.CustomerId);
        if (balance < rate)
        {
            return Fail("insufficient_points", $"Không đủ point để tạo giọng đọc (cần {rate:0}, còn {balance:0}). Nạp thêm point trên dashboard TodoX.");
        }

        VbeeOptions options;
        try
        {
            options = await _vbeeConfig.GetAsync(ct);
            options.GetTokenOrThrow();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Npgsql.NpgsqlException)
        {
            await _usage.RaiseAlertAsync("VBEE_NOT_CONFIGURED", "Cấu hình Vbee của TodoX bị thiếu hoặc lỗi; khách không tạo được giọng đọc.", new { ex.Message }, ct);
            return Fail("voice_service_unavailable", "Dịch vụ giọng đọc TodoX đang tạm ngưng. Vui lòng thử lại sau.");
        }

        var jobId = Guid.NewGuid();
        using (var conn = await _factory.OpenAsync(ct))
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO editvideo.voice_jobs (id, tenant_id, license_id, device_id, customer_id, session_id, voice_code, quality_tier, char_count, points)
                VALUES (@jobId, @tenantId, @licenseId, @deviceId, @customerId, @sessionId, @voiceCode, @tier, @chars, @rate);
                """,
                new { jobId, tenantId = _tenant.TenantId, licenseId = license.Id, deviceId = device.Id, customerId = license.CustomerId, sessionId, voiceCode = voice.Code, tier = QualityTier, chars = text.Length, rate });
        }

        var publicBase = (_configuration["TodoX:PublicBaseUrl"] ?? "https://dashboard.todox.vn").TrimEnd('/');
        var callback = VbeeOptions.BuildAuthorizedCallbackUriOrNull($"{publicBase}/api/editvideo/v1/vbee-callback?job={jobId}", options.CallbackSecret)!.ToString();
        try
        {
            var voiceId = string.IsNullOrWhiteSpace(voice.ProviderVoiceId) ? voice.Code : voice.ProviderVoiceId;
            var submitted = await _vbee.SubmitAsync(new VbeeVoiceSubmitRequest(
                voiceId, text, voice.DefaultRate, null, callback, null,
                options.ResolveSampleRate(voiceId), options.DefaultBitrate, options.DefaultSpeedRate, options.AppId), options, ct);

            await UpdateJobAsync(jobId, "submitted", submitted.RequestId, null, null, null, ct);
            if (!string.IsNullOrWhiteSpace(submitted.AudioUrl))
            {
                return await CompleteAsync(jobId, submitted.AudioUrl, ct);
            }

            return EditVideoResult<EditVideoVoiceJob>.Success(new EditVideoVoiceJob(jobId, "submitted", null, rate, null, null));
        }
        catch (VbeeVoiceSubmitException ex)
        {
            return await FailSubmitAsync(jobId, ex.HttpStatusCode, ex.ErrorCode, ex.ErrorMessage ?? ex.Message, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return await FailSubmitAsync(jobId, null, "VBEE_UNREACHABLE", ex.Message, ct);
        }
    }

    /// <summary>Returns the job, polling Vbee when the callback has not arrived yet.</summary>
    public async Task<EditVideoResult<EditVideoVoiceJob>> GetAsync(Guid jobId, Guid licenseId, CancellationToken ct = default)
    {
        var job = await LoadAsync(jobId, ct);
        if (job is null || job.LicenseId != licenseId)
        {
            return Fail("not_found", "Không tìm thấy yêu cầu giọng đọc.");
        }

        if (job.Status == "submitted" && !string.IsNullOrWhiteSpace(job.ProviderRequestId) && DateTimeOffset.UtcNow - job.CreatedAt > TimeSpan.FromSeconds(4))
        {
            try
            {
                var options = await _vbeeConfig.GetAsync(ct);
                var status = await _vbee.GetStatusAsync(job.ProviderRequestId, options, ct);
                var audio = FindString(status, "audio_link", "audio_url", "audioUrl", "download_url");
                var state = FindString(status, "status", "state")?.ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(audio) && state is null or "SUCCESS" or "SUCCEEDED" or "DONE" or "COMPLETED")
                {
                    return await CompleteAsync(jobId, audio, ct);
                }

                if (state is "FAILURE" or "FAILED" or "ERROR")
                {
                    await UpdateJobAsync(jobId, "failed", null, null, FindString(status, "error_code", "code") ?? "VBEE_FAILED", FindString(status, "error_message", "message"), ct);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "EDITVIDEO_VOICE_POLL_FAILED job={JobId}", jobId);
            }

            job = await LoadAsync(jobId, ct);
        }

        return EditVideoResult<EditVideoVoiceJob>.Success(ToView(job!));
    }

    /// <summary>Vbee callback for jobs this service submitted.</summary>
    public async Task HandleCallbackAsync(Guid jobId, VbeeVoiceCallbackResult payload, CancellationToken ct = default)
    {
        var job = await LoadAsync(jobId, ct);
        if (job is null || job.Status != "submitted")
        {
            return;
        }

        var state = payload.Status?.ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(payload.AudioUrl) && state is null or "SUCCESS" or "SUCCEEDED" or "DONE" or "COMPLETED")
        {
            await CompleteAsync(jobId, payload.AudioUrl, ct);
        }
        else if (state is "FAILURE" or "FAILED" or "ERROR")
        {
            await UpdateJobAsync(jobId, "failed", null, null, payload.ErrorCode ?? "VBEE_FAILED", payload.ErrorMessage, ct);
        }
    }

    private async Task<EditVideoResult<EditVideoVoiceJob>> CompleteAsync(Guid jobId, string audioUrl, CancellationToken ct)
    {
        var job = (await LoadAsync(jobId, ct))!;
        if (!job.Charged)
        {
            var charge = await _wallets.ChargeAsync(job.CustomerId, null, job.Points, 1, Operation, "vbee", job.VoiceCode,
                "editvideo", "voice", referenceId: jobId, referenceType: Operation);
            if (!charge.Ok)
            {
                await UpdateJobAsync(jobId, "failed", null, null, "insufficient_points", "Không đủ point khi hoàn tất giọng đọc.", ct);
                return Fail("insufficient_points", "Không đủ point để nhận giọng đọc. Nạp thêm point trên dashboard TodoX.");
            }

            using var conn = await _factory.OpenAsync(ct);
            await conn.ExecuteAsync("UPDATE editvideo.voice_jobs SET charged = true WHERE id = @jobId;", new { jobId });
        }

        await UpdateJobAsync(jobId, "succeeded", null, audioUrl, null, null, ct);
        return EditVideoResult<EditVideoVoiceJob>.Success(ToView((await LoadAsync(jobId, ct))!));
    }

    private async Task<EditVideoResult<EditVideoVoiceJob>> FailSubmitAsync(Guid jobId, HttpStatusCode? http, string? code, string? message, CancellationToken ct)
    {
        await UpdateJobAsync(jobId, "failed", null, null, code ?? "VBEE_SUBMIT_FAILED", message, ct);
        _logger.LogWarning("EDITVIDEO_VOICE_SUBMIT_FAILED job={JobId} http={Http} code={Code} message={Message}", jobId, (int?)http, code, message);

        if (LooksLikeProviderOutOfCredit(http, code, message))
        {
            await _usage.RaiseAlertAsync("VBEE_ACCOUNT_OUT_OF_CREDIT", "Tài khoản Vbee của TodoX đã hết tiền/hạn mức. Khách không tạo được giọng đọc cho tới khi nạp thêm.", new { http = (int?)http, code, message }, ct);
            return Fail("voice_service_unavailable", "Dịch vụ giọng đọc TodoX đang tạm ngưng (đã báo quản trị viên). Vui lòng thử lại sau.");
        }

        if (http is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            await _usage.RaiseAlertAsync("VBEE_AUTH_FAILED", "Vbee từ chối token của TodoX; cần kiểm tra cấu hình rvideo.vbee.token.", new { http = (int?)http, code }, ct);
            return Fail("voice_service_unavailable", "Dịch vụ giọng đọc TodoX đang tạm ngưng (đã báo quản trị viên). Vui lòng thử lại sau.");
        }

        return Fail("voice_failed", "Không tạo được giọng đọc lúc này. Thử lại sau ít phút.");
    }

    internal static bool LooksLikeProviderOutOfCredit(HttpStatusCode? http, string? code, string? message)
    {
        if (http == HttpStatusCode.PaymentRequired)
        {
            return true;
        }

        var text = $"{code} {message}".ToLowerInvariant();
        return new[] { "balance", "credit", "quota", "insufficient", "exceed", "het tien", "số dư", "so du" }
            .Any(text.Contains);
    }

    private async Task UpdateJobAsync(Guid jobId, string status, string? providerRequestId, string? audioUrl, string? errorCode, string? errorMessage, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE editvideo.voice_jobs
               SET status = @status,
                   provider_request_id = COALESCE(@providerRequestId, provider_request_id),
                   audio_url = COALESCE(@audioUrl, audio_url),
                   error_code = @errorCode,
                   error_message = left(@errorMessage, 1000),
                   completed_at = CASE WHEN @status IN ('succeeded','failed') THEN now() ELSE completed_at END
             WHERE id = @jobId;
            """,
            new { jobId, status, providerRequestId, audioUrl, errorCode, errorMessage });
    }

    private async Task<VoiceJobRow?> LoadAsync(Guid jobId, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<VoiceJobRow>(
            """
            SELECT id AS Id, license_id AS LicenseId, customer_id AS CustomerId, voice_code AS VoiceCode, points AS Points,
                   status AS Status, provider_request_id AS ProviderRequestId, audio_url AS AudioUrl,
                   error_code AS ErrorCode, error_message AS ErrorMessage, charged AS Charged, created_at AS CreatedAt
              FROM editvideo.voice_jobs WHERE id = @jobId AND tenant_id = @tenantId;
            """,
            new { jobId, tenantId = _tenant.TenantId });
    }

    private static EditVideoVoiceJob ToView(VoiceJobRow j) =>
        new(j.Id, j.Status, j.Status == "succeeded" ? j.AudioUrl : null, j.Points, j.ErrorCode,
            j.Status == "failed" ? "Không tạo được giọng đọc." : null);

    private static EditVideoResult<EditVideoVoiceJob> Fail(string code, string message) => EditVideoResult<EditVideoVoiceJob>.Fail(code, message);

    private static string? FindString(JsonNode? node, params string[] keys)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (k, v) in obj)
                {
                    if (keys.Contains(k, StringComparer.OrdinalIgnoreCase) && v is JsonValue jv && jv.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
                    {
                        return s;
                    }
                }

                foreach (var (_, v) in obj)
                {
                    if (FindString(v, keys) is { } found)
                    {
                        return found;
                    }
                }

                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    if (FindString(item, keys) is { } found)
                    {
                        return found;
                    }
                }

                break;
        }

        return null;
    }

    private sealed class VoiceJobRow
    {
        public Guid Id { get; init; }
        public Guid LicenseId { get; init; }
        public Guid CustomerId { get; init; }
        public string VoiceCode { get; init; } = string.Empty;
        public decimal Points { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? ProviderRequestId { get; init; }
        public string? AudioUrl { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public bool Charged { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }
}
