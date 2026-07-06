using System.Text.Json;
using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.ImageRender;

public sealed class MarketingImageRenderLogRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ILogger<MarketingImageRenderLogRepository> _logger;

    public MarketingImageRenderLogRepository(TodoXConnectionFactory factory, TenantContext tenant,
        ILogger<MarketingImageRenderLogRepository> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task SaveAsync(MarketingImageRenderRequest request, MarketingImageRenderResult result, CancellationToken ct = default)
    {
        try
        {
            await _tenant.EnsureLoadedAsync(ct);
            using var conn = await _factory.OpenAsync(ct);
            await conn.ExecuteAsync(
                """
                INSERT INTO render.marketing_image_render_logs
                    (id, tenant_id, user_id, customer_id, render_job_id, log_code, status,
                     service_type, service_name, request_json, render_plan_json, compiled_prompt,
                     result_media_id, result_url, logs_json, error_message, created_at, completed_at)
                VALUES
                    (@id, @tenant, @user, @customer, @renderJobId, @logCode, @status,
                     @serviceType, @serviceName, @requestJson::jsonb, @planJson::jsonb, @compiledPrompt,
                     NULL, @resultUrl, @logsJson::jsonb, @error, now(), now());
                """,
                new
                {
                    id = Guid.NewGuid(),
                    tenant = _tenant.TenantId,
                    user = request.User?.UserId,
                    customer = request.User?.CustomerId,
                    renderJobId = result.RenderJobId,
                    logCode = result.LogCode,
                    status = result.Status,
                    serviceType = result.RenderPlan?.ServiceType,
                    serviceName = result.RenderPlan?.ServiceName ?? request.ServiceName,
                    requestJson = JsonSerializer.Serialize(new
                    {
                        request.ServiceName,
                        request.ServiceCategory,
                        request.ShortDescription,
                        request.Brief,
                        request.Tone,
                        request.AspectRatio,
                        request.BrandRobotImageUrl,
                        request.ReferenceImageUrls,
                        request.PreserveFixedAssets,
                        request.RecreatePlan,
                        result.AnalyzerProvider,
                        result.UsedAnalyzerFallback
                    }, JsonOptions),
                    planJson = JsonSerializer.Serialize(result.UniversalPlan ?? (object?)result.RenderPlan, JsonOptions),
                    compiledPrompt = result.CompiledPrompt,
                    resultUrl = result.ImageUrl,
                    logsJson = JsonSerializer.Serialize(result.Logs, JsonOptions),
                    error = result.Error
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist marketing image render log {LogCode}", result.LogCode);
        }
    }
}
