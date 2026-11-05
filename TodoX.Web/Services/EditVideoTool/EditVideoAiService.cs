using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TodoX.Web.Data;

namespace TodoX.Web.Services.EditVideoTool;

public sealed class EditVideoAiCreateRequest
{
    public string? Model { get; init; }
    public string? Prompt { get; init; }
    public string? Ratio { get; init; }
    public string? Resolution { get; init; }
    public string? Mode { get; init; }
    public string? Duration { get; init; }
    /// <summary>Image: images to edit / subjects. Video: start frame, then end frame.</summary>
    public List<string>? Images { get; init; }
    /// <summary>Reference ("thành phần") images.</summary>
    public List<string>? References { get; init; }
    /// <summary>Video only: source / driving / reference video.</summary>
    public List<string>? VideoUrls { get; init; }
    /// <summary>The tool's retry key: the same key returns the same task instead of paying twice.</summary>
    public string? ClientKey { get; init; }
}

public sealed record EditVideoAiUploadRequest(string? FileName, string? Data);

public sealed record EditVideoAiTask(
    Guid Id, string Kind, string Model, string Status, decimal Points, bool Charged,
    IReadOnlyList<string> Outputs, string? ErrorCode, string? Message, DateTimeOffset CreatedAt);

/// <summary>
/// AI image/video generation for the local tool through TodoX's own Gommo (79AI) account: customers never hold
/// a Gommo key and only ever see their own tasks. Only JSON passes through TodoX — results stay on Gommo's CDN and
/// the tool downloads them straight to the customer's machine. Points are checked before create and charged once,
/// when the task succeeds (same pattern as voice).
/// </summary>
public sealed class EditVideoAiService
{
    public const int MaxPromptChars = 5000;
    public const int MaxInputs = 8;
    private static readonly string[] UploadExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly IEditVideoGommoClient _gommo;
    private readonly WalletService _wallets;
    private readonly EditVideoUsageService _usage;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<EditVideoAiOptions> _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EditVideoAiService> _logger;

    public EditVideoAiService(TodoXConnectionFactory factory, TenantContext tenant, IEditVideoGommoClient gommo, WalletService wallets,
        EditVideoUsageService usage, IMemoryCache cache, IOptionsMonitor<EditVideoAiOptions> options, IConfiguration configuration,
        ILogger<EditVideoAiService> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _gommo = gommo;
        _wallets = wallets;
        _usage = usage;
        _cache = cache;
        _options = options;
        _configuration = configuration;
        _logger = logger;
    }

    private EditVideoAiOptions Options => _options.CurrentValue;

    public decimal ToPoints(decimal credits) => EditVideoGommoJson.Points(
        credits, Options.VndPerCredit, Options.Markup, Options.VndPerPoint ?? _configuration.GetValue("AiImageBilling:TodoXVndPerPoint", 10000m));

    public async Task<EditVideoResult<object>> ModelsAsync(Guid customerId, string? kind, string? model, CancellationToken ct = default)
    {
        if (Disabled() is { } off)
        {
            return EditVideoResult<object>.Fail(off.ErrorCode!, off.Message!);
        }

        kind = Clean(kind);
        model = Clean(model);
        string[] kinds = kind is null ? ["image", "video"] : NormalizeKind(kind) is { } one ? [one] : [];
        if (kinds.Length == 0)
        {
            return EditVideoResult<object>.Fail(EditVideoErrorCodes.InvalidRequest, "type phải là image hoặc video.");
        }

        var list = new List<object>();
        try
        {
            foreach (var k in kinds)
            {
                var catalog = await CatalogAsync(k, ct);
                list.AddRange(EditVideoGommoJson.Models(catalog)
                    .Where(m => model is null || string.Equals(EditVideoGommoJson.Str(m, "model"), model.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Select(m => EditVideoGommoJson.Shape(m, k, ToPoints, withPrices: model is not null)));
            }
        }
        catch (EditVideoGommoException ex)
        {
            return EditVideoResult<object>.Fail((await ProviderFailureAsync(ex, ct)).ErrorCode!, "Danh sách model AI tạm thời không tải được. Thử lại sau ít phút.");
        }

        if (model is not null && list.Count == 0)
        {
            return EditVideoResult<object>.Fail("model_not_found", $"Không có model '{model}'. Gọi lại không kèm model để xem danh sách.");
        }

        return EditVideoResult<object>.Success(new { models = list, balance = await _wallets.GetBalanceAsync(customerId) });
    }

    public async Task<EditVideoResult<EditVideoAiTask>> CreateAsync(
        EditVideoLicenseRow license, EditVideoDeviceRow device, string kind, EditVideoAiCreateRequest request, CancellationToken ct = default)
    {
        if (Disabled() is { } off)
        {
            return off;
        }

        if (Validate(kind, request) is { } invalid)
        {
            return Fail(EditVideoErrorCodes.InvalidRequest, invalid);
        }

        await _tenant.EnsureLoadedAsync(ct);
        var clientKey = string.IsNullOrWhiteSpace(request.ClientKey) ? null : request.ClientKey.Trim();
        if (clientKey is not null && await FindByClientKeyAsync(license.Id, clientKey, ct) is { } existing)
        {
            return await GetAsync(existing, license.Id, ct);
        }

        JsonObject? model;
        try
        {
            model = EditVideoGommoJson.FindModel(await CatalogAsync(kind, ct), request.Model!);
        }
        catch (EditVideoGommoException ex)
        {
            return await ProviderFailureAsync(ex, ct);
        }

        if (model is null)
        {
            return Fail("model_not_found", $"Không có model {kind} '{request.Model}'. Dùng todox_ai_models để xem danh sách.");
        }

        var quote = EditVideoGommoJson.Quote(model, new EditVideoAiSelection(Clean(request.Ratio), Clean(request.Resolution), Clean(request.Mode), Clean(request.Duration)));
        if (!quote.Ok)
        {
            var choices = quote.Choices is { Count: > 0 } c ? $" Lựa chọn {quote.Field}: {string.Join(", ", c)}." : string.Empty;
            return Fail("ai_option_required", quote.Problem! + choices + " Hỏi lại người dùng, không tự chọn.", new { field = quote.Field, choices = quote.Choices });
        }

        var points = ToPoints(quote.Credits);
        var available = await _wallets.GetBalanceAsync(license.CustomerId) - await PendingPointsAsync(license.CustomerId, ct);
        if (available < points)
        {
            return Fail("insufficient_points", $"Không đủ point để tạo {(kind == "image" ? "ảnh" : "video")} AI (cần {points:0.#}, còn khả dụng {Math.Max(0, available):0.#}). Nạp thêm point trên dashboard TodoX.");
        }

        var taskId = Guid.NewGuid();
        var modelCode = EditVideoGommoJson.Str(model, "model")!;
        using (var conn = await _factory.OpenAsync(ct))
        {
            var inserted = await conn.ExecuteAsync(
                """
                INSERT INTO editvideo.ai_tasks (id, tenant_id, license_id, device_id, customer_id, kind, client_key, model, options, credits, points)
                VALUES (@taskId, @tenantId, @licenseId, @deviceId, @customerId, @kind, @clientKey, @modelCode, CAST(@options AS jsonb), @credits, @points)
                ON CONFLICT (license_id, client_key) WHERE client_key IS NOT NULL DO NOTHING;
                """,
                new
                {
                    taskId, tenantId = _tenant.TenantId, licenseId = license.Id, deviceId = device.Id, customerId = license.CustomerId,
                    kind, clientKey, modelCode, options = JsonSerializer.Serialize(quote.Selection), credits = quote.Credits, points
                });
            if (inserted == 0 && clientKey is not null && await FindByClientKeyAsync(license.Id, clientKey, ct) is { } raced)
            {
                return await GetAsync(raced, license.Id, ct);
            }
        }

        try
        {
            var created = await _gommo.PostAsync(kind == "image" ? "/generateImage" : "/create-video", CreateFields(kind, modelCode, request, quote.Selection), ct);
            var idBase = EditVideoGommoJson.IdBase(created)
                ?? throw new EditVideoGommoException("no_task_id", EditVideoGommoJson.ErrorMessage(created) ?? "Gommo did not return a task id.");
            await UpdateAsync(taskId, "running", idBase, null, null, null, ct);
            _logger.LogInformation("EDITVIDEO_AI_CREATED task={TaskId} kind={Kind} model={Model} points={Points}", taskId, kind, modelCode, points);
        }
        catch (EditVideoGommoException ex)
        {
            await UpdateAsync(taskId, "failed", null, null, ex.Code, ex.Message, ct);
            return await ProviderFailureAsync(ex, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await UpdateAsync(taskId, "failed", null, null, "unreachable", ex.Message, ct);
            return Fail("ai_failed", "Không kết nối được dịch vụ AI lúc này. Thử lại sau ít phút.");
        }

        return EditVideoResult<EditVideoAiTask>.Success(ToView((await LoadAsync(taskId, ct))!));
    }

    /// <summary>The customer's own task, refreshed from Gommo (at most every few seconds) while it is still running.</summary>
    public async Task<EditVideoResult<EditVideoAiTask>> GetAsync(Guid taskId, Guid licenseId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        var row = await LoadAsync(taskId, ct);
        if (row is null || row.LicenseId != licenseId)
        {
            return Fail("not_found", "Không tìm thấy tác vụ AI này.");
        }

        if (row.Status == "running" && row.GommoIdBase is not null
            && !_cache.TryGetValue(CacheKey(taskId), out _))
        {
            _cache.Set(CacheKey(taskId), true, TimeSpan.FromSeconds(Math.Max(1, Options.StatusCacheSeconds)));
            await RefreshAsync(row, ct);
            row = (await LoadAsync(taskId, ct))!;
        }

        return EditVideoResult<EditVideoAiTask>.Success(ToView(row));
    }

    public async Task<EditVideoResult<object>> ListAsync(Guid licenseId, string? kind, int limit, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        var normalized = kind is null ? null : NormalizeKind(kind);
        using var conn = await _factory.OpenAsync(ct);
        var ids = (await conn.QueryAsync<Guid>(
            """
            SELECT id FROM editvideo.ai_tasks
             WHERE tenant_id = @tenantId AND license_id = @licenseId AND (@kind::text IS NULL OR kind = @kind)
             ORDER BY created_at DESC LIMIT @limit;
            """,
            new { tenantId = _tenant.TenantId, licenseId, kind = normalized, limit = Math.Clamp(limit, 1, 50) })).ToList();

        var tasks = new List<EditVideoAiTask>();
        foreach (var id in ids)
        {
            var task = await GetAsync(id, licenseId, ct);
            tasks.Add(task.Value!);
        }

        return EditVideoResult<object>.Success(new { tasks });
    }

    /// <summary>
    /// Passes a local image from the customer's machine to Gommo's own upload and returns the hosted URL;
    /// nothing is written on TodoX's server.
    /// </summary>
    public async Task<EditVideoResult<object>> UploadAsync(EditVideoAiUploadRequest request, CancellationToken ct = default)
    {
        if (Disabled() is { } off)
        {
            return EditVideoResult<object>.Fail(off.ErrorCode!, off.Message!);
        }

        var fileName = Path.GetFileName(request.FileName ?? string.Empty);
        if (!UploadExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase))
        {
            return EditVideoResult<object>.Fail(EditVideoErrorCodes.InvalidRequest, "Chỉ tải lên ảnh .jpg, .png hoặc .webp.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(request.Data ?? string.Empty);
        }
        catch (FormatException)
        {
            return EditVideoResult<object>.Fail(EditVideoErrorCodes.InvalidRequest, "Dữ liệu ảnh không hợp lệ.");
        }

        if (bytes.Length == 0 || bytes.Length > Options.MaxUploadBytes)
        {
            return EditVideoResult<object>.Fail(EditVideoErrorCodes.InvalidRequest, $"Ảnh phải nhỏ hơn {Options.MaxUploadBytes / 1024 / 1024} MB.");
        }

        try
        {
            var uploaded = await _gommo.PostAsync("/image-upload", new Dictionary<string, string?>
            {
                ["data"] = request.Data,
                ["project_id"] = Options.ProjectId,
                ["file_name"] = fileName,
                ["size"] = bytes.Length.ToString()
            }, ct);
            var url = EditVideoGommoJson.Str(uploaded["imageInfo"], "url") ?? EditVideoGommoJson.Str(uploaded["data"], "url") ?? EditVideoGommoJson.Str(uploaded, "url");
            return url is null
                ? EditVideoResult<object>.Fail("ai_failed", EditVideoGommoJson.ErrorMessage(uploaded) ?? "Không tải được ảnh lên.")
                : EditVideoResult<object>.Success(new { url });
        }
        catch (EditVideoGommoException ex)
        {
            return EditVideoResult<object>.Fail((await ProviderFailureAsync(ex, ct)).ErrorCode!, "Không tải được ảnh lên dịch vụ AI lúc này.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return EditVideoResult<object>.Fail("ai_failed", "Không kết nối được dịch vụ AI lúc này. Thử lại sau ít phút.");
        }
    }

    internal static Dictionary<string, string?> CreateFields(string kind, string model, EditVideoAiCreateRequest request, EditVideoAiSelection selection)
    {
        var images = Urls(request.Images);
        var references = Urls(request.References);
        var fields = new Dictionary<string, string?>
        {
            ["model"] = model,
            ["prompt"] = request.Prompt?.Trim(),
            ["ratio"] = selection.Ratio,
            ["resolution"] = selection.Resolution,
            ["mode"] = selection.Mode,
            ["privacy"] = "PRIVATE",
            ["project_id"] = "default"
        };

        if (kind == "image")
        {
            fields["action_type"] = "create";
            var inputs = images.Concat(references).ToList();
            if (inputs.Count > 0)
            {
                fields["editImage"] = "true";
                fields["subjects"] = JsonSerializer.Serialize(inputs);
            }

            return fields;
        }

        fields["type"] = "video";
        fields["duration"] = selection.Duration;
        fields["translate_to_en"] = "false";
        if (images.Count > 0)
        {
            fields["images"] = JsonSerializer.Serialize(images.Select(url => new { url }));
        }

        if (references.Count > 0)
        {
            fields["references"] = JsonSerializer.Serialize(references.Select(url => new { url }));
        }

        var videos = Urls(request.VideoUrls);
        if (videos.Count > 0)
        {
            fields["video_url"] = videos[0];
            fields["video_urls"] = JsonSerializer.Serialize(videos.Select(url => new { url }));
        }

        return fields;
    }

    internal static string? Validate(string kind, EditVideoAiCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return "Cần chọn model (xem todox_ai_models).";
        }

        if (kind == "image" && string.IsNullOrWhiteSpace(request.Prompt))
        {
            return "Cần mô tả (prompt) cho ảnh.";
        }

        if ((request.Prompt?.Length ?? 0) > MaxPromptChars)
        {
            return $"Prompt tối đa {MaxPromptChars} ký tự.";
        }

        var all = (request.Images ?? []).Concat(request.References ?? []).Concat(request.VideoUrls ?? []).ToList();
        if (all.Count > MaxInputs)
        {
            return $"Tối đa {MaxInputs} ảnh/video đầu vào.";
        }

        if (all.Any(u => !Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
        {
            return "Ảnh/video đầu vào phải là link https công khai (file trên máy được TodoX tải lên trước).";
        }

        if (kind == "image" && request.VideoUrls is { Count: > 0 })
        {
            return "Tạo ảnh không nhận video đầu vào.";
        }

        return request.ClientKey is { Length: > 128 } ? "clientKey tối đa 128 ký tự." : null;
    }

    private async Task RefreshAsync(AiTaskRow row, CancellationToken ct)
    {
        JsonObject? task;
        try
        {
            if (row.Kind == "image")
            {
                task = EditVideoGommoJson.FindTask(await _gommo.PostAsync("/image", new Dictionary<string, string?> { ["id_base"] = row.GommoIdBase }, ct), row.GommoIdBase!);
            }
            else
            {
                var fields = new Dictionary<string, string?> { ["id_base"] = row.GommoIdBase, ["videoId"] = row.GommoIdBase, ["project_id"] = Options.ProjectId };
                task = EditVideoGommoJson.FindTask(await _gommo.PostAsync("/video", fields, ct), row.GommoIdBase!)
                    ?? EditVideoGommoJson.FindTask(await _gommo.PostAsync("/videos", new Dictionary<string, string?> { ["project_id"] = Options.ProjectId }, ct), row.GommoIdBase!);
            }
        }
        catch (Exception ex) when (ex is EditVideoGommoException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "EDITVIDEO_AI_POLL_FAILED task={TaskId}", row.Id);
            return;
        }

        if (task is null)
        {
            return;
        }

        var status = EditVideoGommoJson.Status(task);
        if (status == "failed")
        {
            await UpdateAsync(row.Id, "failed", null, null, "generation_failed", EditVideoGommoJson.ErrorMessage(task), ct);
            return;
        }

        var outputs = EditVideoGommoJson.Outputs(task, row.Kind);
        if (status != "succeeded" || outputs.Count == 0)
        {
            return;
        }

        if (!row.Charged)
        {
            var operation = $"editvideo_ai_{row.Kind}";
            var charge = await _wallets.ChargeAsync(row.CustomerId, null, row.Points, 1, operation, "79ai", row.Model,
                "editvideo", row.Kind, referenceId: row.Id, referenceType: operation);
            if (!charge.Ok)
            {
                await UpdateAsync(row.Id, "failed", null, null, "insufficient_points", "Không đủ point khi hoàn tất.", ct);
                return;
            }

            using var conn = await _factory.OpenAsync(ct);
            await conn.ExecuteAsync("UPDATE editvideo.ai_tasks SET charged = true WHERE id = @id;", new { id = row.Id });
        }

        await UpdateAsync(row.Id, "succeeded", null, outputs, null, null, ct);
    }

    private async Task<JsonNode> CatalogAsync(string kind, CancellationToken ct)
    {
        var key = $"editvideo-ai-models:{kind}";
        if (_cache.TryGetValue(key, out JsonNode? cached) && cached is not null)
        {
            return cached;
        }

        var fresh = await _gommo.PostAsync("/models", new Dictionary<string, string?> { ["type"] = kind }, ct);
        _cache.Set(key, fresh, TimeSpan.FromSeconds(Math.Max(1, Options.ModelsCacheSeconds)));
        return fresh;
    }

    /// <summary>Points promised to tasks still running (not yet charged), so parallel creates cannot overspend.</summary>
    private async Task<decimal> PendingPointsAsync(Guid customerId, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<decimal>(
            """
            SELECT COALESCE(SUM(points), 0) FROM editvideo.ai_tasks
             WHERE tenant_id = @tenantId AND customer_id = @customerId AND NOT charged
               AND status IN ('submitting', 'running') AND created_at > now() - interval '2 hours';
            """,
            new { tenantId = _tenant.TenantId, customerId });
    }

    private async Task<EditVideoResult<EditVideoAiTask>> ProviderFailureAsync(EditVideoGommoException ex, CancellationToken ct)
    {
        _logger.LogWarning("EDITVIDEO_AI_PROVIDER_FAILED code={Code} http={Http} message={Message}", ex.Code, (int?)ex.Status, ex.Message);
        if (ex.Code == "not_configured")
        {
            await _usage.RaiseAlertAsync("GOMMO_NOT_CONFIGURED", "Credential 79AI (Gommo) của TodoX bị thiếu; khách không tạo được ảnh/video AI.", new { ex.Message }, ct);
            return Fail("ai_service_unavailable", "Dịch vụ AI của TodoX đang tạm ngưng. Vui lòng thử lại sau.");
        }

        if (EditVideoVoiceService.LooksLikeProviderOutOfCredit(ex.Status, ex.Code, ex.Message))
        {
            await _usage.RaiseAlertAsync("GOMMO_ACCOUNT_OUT_OF_CREDIT", "Tài khoản Gommo (79AI) của TodoX đã hết credit. Khách không tạo được ảnh/video AI cho tới khi nạp thêm.", new { http = (int?)ex.Status, ex.Code, ex.Message }, ct);
            return Fail("ai_service_unavailable", "Dịch vụ AI của TodoX đang tạm ngưng (đã báo quản trị viên). Vui lòng thử lại sau.");
        }

        if (ex.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            await _usage.RaiseAlertAsync("GOMMO_AUTH_FAILED", "Gommo (79AI) từ chối access token của TodoX; cần kiểm tra credential 79ai.", new { http = (int?)ex.Status, ex.Code }, ct);
            return Fail("ai_service_unavailable", "Dịch vụ AI của TodoX đang tạm ngưng (đã báo quản trị viên). Vui lòng thử lại sau.");
        }

        return Fail("ai_failed", $"Dịch vụ AI từ chối yêu cầu: {ex.Message}");
    }

    private EditVideoResult<EditVideoAiTask>? Disabled() =>
        Options.Enabled ? null : Fail("ai_service_unavailable", "Tính năng tạo ảnh/video AI đang tắt.");

    private async Task<Guid?> FindByClientKeyAsync(Guid licenseId, string clientKey, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<Guid?>(
            "SELECT id FROM editvideo.ai_tasks WHERE license_id = @licenseId AND client_key = @clientKey;",
            new { licenseId, clientKey });
    }

    private async Task UpdateAsync(Guid id, string status, string? idBase, IReadOnlyList<string>? outputs, string? errorCode, string? errorMessage, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE editvideo.ai_tasks
               SET status = @status,
                   gommo_id_base = COALESCE(@idBase, gommo_id_base),
                   outputs = COALESCE(CAST(@outputs AS jsonb), outputs),
                   error_code = @errorCode,
                   error_message = left(@errorMessage, 1000),
                   completed_at = CASE WHEN @status IN ('succeeded','failed') THEN now() ELSE completed_at END
             WHERE id = @id;
            """,
            new { id, status, idBase, outputs = outputs is null ? null : JsonSerializer.Serialize(outputs), errorCode, errorMessage });
    }

    private async Task<AiTaskRow?> LoadAsync(Guid id, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AiTaskRow>(
            """
            SELECT id AS Id, license_id AS LicenseId, customer_id AS CustomerId, kind AS Kind, model AS Model, points AS Points,
                   status AS Status, gommo_id_base AS GommoIdBase, outputs::text AS OutputsJson, error_code AS ErrorCode,
                   error_message AS ErrorMessage, charged AS Charged, created_at AS CreatedAt
              FROM editvideo.ai_tasks WHERE id = @id AND tenant_id = @tenantId;
            """,
            new { id, tenantId = _tenant.TenantId });
    }

    private static EditVideoAiTask ToView(AiTaskRow r) => new(
        r.Id, r.Kind, r.Model, r.Status == "submitting" ? "running" : r.Status, r.Points, r.Charged,
        r.Status == "succeeded" && r.OutputsJson is not null ? JsonSerializer.Deserialize<List<string>>(r.OutputsJson) ?? [] : [],
        r.Status == "failed" ? r.ErrorCode : null,
        r.Status == "failed" ? FailureMessage(r) : null,
        r.CreatedAt);

    private static string FailureMessage(AiTaskRow r) => r.ErrorCode switch
    {
        "insufficient_points" => "Không đủ point để nhận kết quả. Nạp thêm point trên dashboard TodoX.",
        "generation_failed" when !string.IsNullOrWhiteSpace(r.ErrorMessage) => $"AI không tạo được kết quả: {r.ErrorMessage}",
        _ => "AI không tạo được kết quả. Không bị trừ point."
    };

    private static List<string> Urls(List<string>? values) =>
        (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static string? NormalizeKind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "image" or "images" => "image",
        "video" or "videos" => "video",
        _ => null
    };

    private static string CacheKey(Guid taskId) => $"editvideo-ai-status:{taskId}";

    private static EditVideoResult<EditVideoAiTask> Fail(string code, string message, object? detail = null) =>
        EditVideoResult<EditVideoAiTask>.Fail(code, message, detail);

    private sealed class AiTaskRow
    {
        public Guid Id { get; init; }
        public Guid LicenseId { get; init; }
        public Guid CustomerId { get; init; }
        public string Kind { get; init; } = string.Empty;
        public string Model { get; init; } = string.Empty;
        public decimal Points { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? GommoIdBase { get; init; }
        public string? OutputsJson { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public bool Charged { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }
}
