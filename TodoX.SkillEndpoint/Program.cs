using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TodoX.SkillEndpoint;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.Configure<SkillEndpointOptions>(builder.Configuration.GetSection(SkillEndpointOptions.SectionName));
builder.Services.AddHttpClient<TodoXOperationsClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config[$"{SkillEndpointOptions.SectionName}:TodoXOperationsBaseUrl"];
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    }
    client.Timeout = TimeSpan.FromSeconds(90);
});
builder.Services.AddSingleton<SkillAuditLog>();

var app = builder.Build();

app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/health") ||
        context.Request.Path.StartsWithSegments("/openapi"))
    {
        await next();
        return;
    }

    var options = context.RequestServices
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<SkillEndpointOptions>>().Value;

    var supplied = context.Request.Headers["X-TodoX-Skill-Key"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(options.ApiKey) ||
        string.IsNullOrWhiteSpace(supplied) ||
        !FixedTimeEquals(options.ApiKey, supplied))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            success = false,
            error = "UNAUTHORIZED",
            message = "X-TodoX-Skill-Key không hợp lệ."
        });
        return;
    }

    await next();
});

app.MapGet("/health", () => Results.Ok(new
{
    service = "todox-skill-endpoint",
    status = "ok",
    utc = DateTimeOffset.UtcNow
}));

var api = app.MapGroup("/api/skill/v1")
    .WithTags("TodoX Skill API");

api.MapGet("/jobs/{jobId:long}", async (long jobId, TodoXOperationsClient client, CancellationToken ct) =>
{
    return await Proxy(() => client.GetJobAsync(jobId, ct));
})
.WithName("GetRenderJob")
.WithSummary("Đọc snapshot đầy đủ của một render job và các scene.");

api.MapGet("/jobs/{jobId:long}/diagnostic", async (long jobId, TodoXOperationsClient client, CancellationToken ct) =>
{
    return await Proxy(() => client.DiagnoseJobAsync(jobId, ct));
})
.WithName("DiagnoseRenderJob")
.WithSummary("Chẩn đoán trạng thái job, scene, provider task, polling, retry và billing.");

api.MapPost("/jobs/{jobId:long}/repair-plan", async (
    long jobId,
    RepairPlanRequest body,
    TodoXOperationsClient client,
    CancellationToken ct) =>
{
    return await Proxy(() => client.CreateRepairPlanAsync(jobId, body, ct));
})
.WithName("CreateRepairPlan")
.WithSummary("Tạo kế hoạch sửa job nhưng chưa thay đổi dữ liệu.");

api.MapPost("/jobs/{jobId:long}/retry", async (
    HttpContext http,
    long jobId,
    RetryJobRequest body,
    TodoXOperationsClient client,
    SkillAuditLog audit,
    CancellationToken ct) =>
{
    var idempotencyKey = RequireIdempotencyKey(http);
    if (idempotencyKey is null)
        return Results.BadRequest(new { success = false, error = "IDEMPOTENCY_KEY_REQUIRED" });

    var result = await client.RetryJobAsync(jobId, body, idempotencyKey, ct);
    await audit.WriteAsync(http, "retry_job", jobId, body, result.StatusCode, ct);
    return ToResult(result);
})
.WithName("RetryRenderJob")
.WithSummary("Retry scene lỗi hoặc scene chỉ định; không retry scene đã thành công nếu không yêu cầu rõ.");

api.MapPost("/jobs/{jobId:long}/resume", async (
    HttpContext http,
    long jobId,
    ResumeJobRequest body,
    TodoXOperationsClient client,
    SkillAuditLog audit,
    CancellationToken ct) =>
{
    var idempotencyKey = RequireIdempotencyKey(http);
    if (idempotencyKey is null)
        return Results.BadRequest(new { success = false, error = "IDEMPOTENCY_KEY_REQUIRED" });

    var result = await client.ResumeJobAsync(jobId, body, idempotencyKey, ct);
    await audit.WriteAsync(http, "resume_job", jobId, body, result.StatusCode, ct);
    return ToResult(result);
})
.WithName("ResumeRenderJob")
.WithSummary("Tiếp tục job từ trạng thái hiện tại mà không tạo lại media đã thành công.");

api.MapPost("/jobs/{jobId:long}/repair", async (
    HttpContext http,
    long jobId,
    ExecuteRepairRequest body,
    TodoXOperationsClient client,
    SkillAuditLog audit,
    CancellationToken ct) =>
{
    var idempotencyKey = RequireIdempotencyKey(http);
    if (idempotencyKey is null)
        return Results.BadRequest(new { success = false, error = "IDEMPOTENCY_KEY_REQUIRED" });

    if (!body.Confirm)
    {
        return Results.BadRequest(new
        {
            success = false,
            error = "CONFIRM_REQUIRED",
            message = "Repair thay đổi dữ liệu nên confirm=true là bắt buộc."
        });
    }

    var result = await client.ExecuteRepairAsync(jobId, body, idempotencyKey, ct);
    await audit.WriteAsync(http, "repair_job", jobId, body, result.StatusCode, ct);
    return ToResult(result);
})
.WithName("RepairRenderJob")
.WithSummary("Sửa state job/scene bằng action đã whitelist; không cho chạy SQL tự do.");

api.MapPost("/jobs/{jobId:long}/reconcile", async (
    HttpContext http,
    long jobId,
    ReconcileJobRequest body,
    TodoXOperationsClient client,
    SkillAuditLog audit,
    CancellationToken ct) =>
{
    var idempotencyKey = RequireIdempotencyKey(http);
    if (idempotencyKey is null)
        return Results.BadRequest(new { success = false, error = "IDEMPOTENCY_KEY_REQUIRED" });

    var result = await client.ReconcileJobAsync(jobId, body, idempotencyKey, ct);
    await audit.WriteAsync(http, "reconcile_job", jobId, body, result.StatusCode, ct);
    return ToResult(result);
})
.WithName("ReconcileRenderJob")
.WithSummary("Đối soát lại provider task và đồng bộ trạng thái local khi polling bị timeout hoặc lệch state.");

api.MapGet("/actions/{actionId}", async (string actionId, TodoXOperationsClient client, CancellationToken ct) =>
{
    return await Proxy(() => client.GetActionAsync(actionId, ct));
})
.WithName("GetSkillAction")
.WithSummary("Theo dõi action bất đồng bộ như retry, resume hoặc repair.");

app.Run();

static bool FixedTimeEquals(string expected, string actual)
{
    var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
    var b = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
    return CryptographicOperations.FixedTimeEquals(a, b);
}

static string? RequireIdempotencyKey(HttpContext http)
{
    var value = http.Request.Headers["X-Idempotency-Key"].FirstOrDefault();
    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

static async Task<IResult> Proxy(Func<Task<ProxyResponse>> action)
{
    var response = await action();
    return ToResult(response);
}

static IResult ToResult(ProxyResponse response)
{
    if (response.Body.ValueKind == JsonValueKind.Undefined)
    {
        return Results.StatusCode(response.StatusCode);
    }

    return Results.Json(response.Body, statusCode: response.StatusCode);
}
