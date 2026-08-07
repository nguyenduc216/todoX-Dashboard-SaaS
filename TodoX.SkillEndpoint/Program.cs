using System.Security.Cryptography;
using System.Text;
using TodoX.SkillEndpoint;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.Configure<SkillEndpointOptions>(builder.Configuration.GetSection(SkillEndpointOptions.SectionName));
builder.Services.AddSingleton<SkillAuditLog>();
builder.Services.AddSingleton<SkillDatabase>();
builder.Services.AddScoped<SkillDiagnosticRepository>();

var app = builder.Build();

app.UseHttpsRedirection();

app.MapOpenApi();

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

app.MapGet("/health", async (SkillDatabase db, CancellationToken ct) =>
{
    try
    {
        await using var cn = db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new Npgsql.NpgsqlCommand("select 1", cn);
        await cmd.ExecuteScalarAsync(ct);
        return Results.Ok(new
        {
            service = "todox-skill-endpoint",
            status = "ok",
            database = "ok",
            version = "0.2.0",
            utc = DateTimeOffset.UtcNow
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            service = "todox-skill-endpoint",
            status = "degraded",
            database = "error",
            error = ex.Message,
            utc = DateTimeOffset.UtcNow
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

var api = app.MapGroup("/api/skill/v1")
    .WithTags("TodoX Skill API");

api.MapGet("/jobs/{jobId}", async (string jobId, SkillDiagnosticRepository repo, CancellationToken ct) =>
{
    var result = await repo.GetJobSnapshotAsync(jobId, ct);
    return result is null
        ? Results.NotFound(new { success = false, error = "JOB_NOT_FOUND", jobId })
        : Results.Json(result);
})
.WithName("GetRenderJob")
.WithSummary("Đọc snapshot đầy đủ của job, scene, render task, queue và log.");

api.MapGet("/jobs/{jobId}/diagnostic", async (string jobId, SkillDiagnosticRepository repo, CancellationToken ct) =>
{
    var result = await repo.DiagnoseAsync(jobId, ct);
    return result is null
        ? Results.NotFound(new { success = false, error = "JOB_NOT_FOUND", jobId })
        : Results.Json(result);
})
.WithName("DiagnoseRenderJob")
.WithSummary("Chẩn đoán failed/TIMEOUT_PENDING và state mismatch để xác định scene retryable.");

api.MapPost("/jobs/{jobId}/repair-plan", async (
    string jobId,
    RepairPlanRequest body,
    SkillDiagnosticRepository repo,
    CancellationToken ct) =>
{
    var result = await repo.BuildRepairPlanAsync(jobId, body, ct);
    return result is null
        ? Results.NotFound(new { success = false, error = "JOB_NOT_FOUND", jobId })
        : Results.Json(result);
})
.WithName("CreateRepairPlan")
.WithSummary("Tạo kế hoạch sửa nhưng chưa thay đổi dữ liệu.");

api.MapPost("/jobs/{jobId}/retry", async (
    HttpContext http,
    string jobId,
    RetryJobRequest body,
    SkillDiagnosticRepository repo,
    SkillAuditLog audit,
    CancellationToken ct) =>
{
    var idempotencyKey = RequireIdempotencyKey(http);
    if (idempotencyKey is null)
        return Results.BadRequest(new { success = false, error = "IDEMPOTENCY_KEY_REQUIRED" });

    var diagnostic = await repo.DiagnoseAsync(jobId, ct);
    if (diagnostic is null)
        return Results.NotFound(new { success = false, error = "JOB_NOT_FOUND", jobId });

    var queued = await repo.EnqueueActionAsync(jobId, "retry", body, idempotencyKey, "skill-api", ct);
    await audit.WriteAsync(http, "retry_job_queued", ParseNumericJobId(jobId), body, StatusCodes.Status202Accepted, ct);
    return Results.Accepted(value: queued);
})
.WithName("RetryRenderJob")
.WithSummary("Đưa yêu cầu retry vào action queue; worker executor sẽ reconcile trước khi submit provider mới.");

api.MapPost("/jobs/{jobId}/resume", async (
    HttpContext http,
    string jobId,
    ResumeJobRequest body,
    SkillDiagnosticRepository repo,
    SkillAuditLog audit,
    CancellationToken ct) =>
{
    var idempotencyKey = RequireIdempotencyKey(http);
    if (idempotencyKey is null)
        return Results.BadRequest(new { success = false, error = "IDEMPOTENCY_KEY_REQUIRED" });

    var snapshot = await repo.GetJobSnapshotAsync(jobId, ct);
    if (snapshot is null)
        return Results.NotFound(new { success = false, error = "JOB_NOT_FOUND", jobId });

    var queued = await repo.EnqueueActionAsync(jobId, "resume", body, idempotencyKey, "skill-api", ct);
    await audit.WriteAsync(http, "resume_job_queued", ParseNumericJobId(jobId), body, StatusCodes.Status202Accepted, ct);
    return Results.Accepted(value: queued);
})
.WithName("ResumeRenderJob")
.WithSummary("Đưa yêu cầu resume vào action queue, không tạo lại media đã success.");

api.MapPost("/jobs/{jobId}/reconcile", async (
    HttpContext http,
    string jobId,
    ReconcileJobRequest body,
    SkillDiagnosticRepository repo,
    SkillAuditLog audit,
    CancellationToken ct) =>
{
    var idempotencyKey = RequireIdempotencyKey(http);
    if (idempotencyKey is null)
        return Results.BadRequest(new { success = false, error = "IDEMPOTENCY_KEY_REQUIRED" });

    var snapshot = await repo.GetJobSnapshotAsync(jobId, ct);
    if (snapshot is null)
        return Results.NotFound(new { success = false, error = "JOB_NOT_FOUND", jobId });

    var queued = await repo.EnqueueActionAsync(jobId, "reconcile", body, idempotencyKey, "skill-api", ct);
    await audit.WriteAsync(http, "reconcile_job_queued", ParseNumericJobId(jobId), body, StatusCodes.Status202Accepted, ct);
    return Results.Accepted(value: queued);
})
.WithName("ReconcileRenderJob")
.WithSummary("Đưa yêu cầu đối soát provider/local state vào action queue.");

api.MapPost("/jobs/{jobId}/repair", async (
    HttpContext http,
    string jobId,
    ExecuteRepairRequest body,
    SkillDiagnosticRepository repo,
    SkillAuditLog audit,
    CancellationToken ct) =>
{
    var idempotencyKey = RequireIdempotencyKey(http);
    if (idempotencyKey is null)
        return Results.BadRequest(new { success = false, error = "IDEMPOTENCY_KEY_REQUIRED" });
    if (!body.Confirm)
        return Results.BadRequest(new { success = false, error = "CONFIRM_REQUIRED" });
    if (!AllowedRepairCodes.Contains(body.RepairCode))
        return Results.BadRequest(new { success = false, error = "REPAIR_CODE_NOT_ALLOWED", allowed = AllowedRepairCodes });

    var snapshot = await repo.GetJobSnapshotAsync(jobId, ct);
    if (snapshot is null)
        return Results.NotFound(new { success = false, error = "JOB_NOT_FOUND", jobId });

    var queued = await repo.EnqueueActionAsync(jobId, "repair", body, idempotencyKey, "skill-api", ct);
    await audit.WriteAsync(http, "repair_job_queued", ParseNumericJobId(jobId), body, StatusCodes.Status202Accepted, ct);
    return Results.Accepted(value: queued);
})
.WithName("RepairRenderJob")
.WithSummary("Đưa repair whitelist vào action queue; không cho arbitrary SQL.");

api.MapGet("/actions/{actionId}", async (string actionId, SkillDiagnosticRepository repo, CancellationToken ct) =>
{
    var result = await repo.GetActionAsync(actionId, ct);
    return result is null
        ? Results.NotFound(new { success = false, error = "ACTION_NOT_FOUND", actionId })
        : Results.Json(result);
})
.WithName("GetSkillAction")
.WithSummary("Theo dõi trạng thái action retry/resume/reconcile/repair.");

app.Run();

static readonly HashSet<string> AllowedRepairCodes = new(StringComparer.OrdinalIgnoreCase)
{
    "MARK_TIMEOUT_SCENE_RETRYABLE",
    "CLEAR_STALE_SCENE_LOCK",
    "RESET_FAILED_SCENE_TO_QUEUED",
    "REBUILD_JOB_SUMMARY",
    "REQUEUE_VIDEO_WORKER",
    "REQUEUE_FINALIZER",
    "REQUEUE_MERGE"
};

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

static long ParseNumericJobId(string jobId) => long.TryParse(jobId, out var value) ? value : 0;
