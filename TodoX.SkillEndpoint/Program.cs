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
var allowedRepairCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "MARK_TIMEOUT_SCENE_RETRYABLE", "CLEAR_STALE_SCENE_LOCK", "RESET_FAILED_SCENE_TO_QUEUED",
    "REBUILD_JOB_SUMMARY", "REQUEUE_VIDEO_WORKER", "REQUEUE_FINALIZER", "REQUEUE_MERGE"
};

app.UseHttpsRedirection();
app.MapOpenApi();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/health") || context.Request.Path.StartsWithSegments("/openapi"))
    {
        await next();
        return;
    }

    var options = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<SkillEndpointOptions>>().Value;
    var supplied = context.Request.Headers["X-TodoX-Skill-Key"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(supplied) || !FixedTimeEquals(options.ApiKey, supplied))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { success = false, error = "UNAUTHORIZED" });
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
        return Results.Ok(new { service = "todox-skill-endpoint", status = "ok", database = "ok", version = "0.2.0", utc = DateTimeOffset.UtcNow });
    }
    catch (Exception ex)
    {
        return Results.Json(new { service = "todox-skill-endpoint", status = "degraded", database = "error", error = ex.Message }, statusCode: 503);
    }
});

var api = app.MapGroup("/api/skill/v1").WithTags("TodoX Skill API");

api.MapGet("/jobs/{jobId}", async (string jobId, SkillDiagnosticRepository repo, CancellationToken ct) =>
{
    var r = await repo.GetJobSnapshotAsync(jobId, ct);
    return r is null ? Results.NotFound(new { success = false, error = "JOB_NOT_FOUND", jobId }) : Results.Json(r);
});

api.MapGet("/jobs/{jobId}/diagnostic", async (string jobId, SkillDiagnosticRepository repo, CancellationToken ct) =>
{
    var r = await repo.DiagnoseAsync(jobId, ct);
    return r is null ? Results.NotFound(new { success = false, error = "JOB_NOT_FOUND", jobId }) : Results.Json(r);
});

api.MapPost("/jobs/{jobId}/repair-plan", async (string jobId, RepairPlanRequest body, SkillDiagnosticRepository repo, CancellationToken ct) =>
{
    var r = await repo.BuildRepairPlanAsync(jobId, body, ct);
    return r is null ? Results.NotFound(new { success = false, error = "JOB_NOT_FOUND", jobId }) : Results.Json(r);
});

api.MapPost("/jobs/{jobId}/retry", async (HttpContext http, string jobId, RetryJobRequest body, SkillDiagnosticRepository repo, SkillAuditLog audit, CancellationToken ct) =>
    await QueueAction(http, jobId, "retry", body, repo, audit, ct));

api.MapPost("/jobs/{jobId}/resume", async (HttpContext http, string jobId, ResumeJobRequest body, SkillDiagnosticRepository repo, SkillAuditLog audit, CancellationToken ct) =>
    await QueueAction(http, jobId, "resume", body, repo, audit, ct));

api.MapPost("/jobs/{jobId}/reconcile", async (HttpContext http, string jobId, ReconcileJobRequest body, SkillDiagnosticRepository repo, SkillAuditLog audit, CancellationToken ct) =>
    await QueueAction(http, jobId, "reconcile", body, repo, audit, ct));

api.MapPost("/jobs/{jobId}/repair", async (HttpContext http, string jobId, ExecuteRepairRequest body, SkillDiagnosticRepository repo, SkillAuditLog audit, CancellationToken ct) =>
{
    if (!body.Confirm) return Results.BadRequest(new { success = false, error = "CONFIRM_REQUIRED" });
    if (!allowedRepairCodes.Contains(body.RepairCode)) return Results.BadRequest(new { success = false, error = "REPAIR_CODE_NOT_ALLOWED", allowed = allowedRepairCodes });
    return await QueueAction(http, jobId, "repair", body, repo, audit, ct);
});

api.MapGet("/actions/{actionId}", async (string actionId, SkillDiagnosticRepository repo, CancellationToken ct) =>
{
    var r = await repo.GetActionAsync(actionId, ct);
    return r is null ? Results.NotFound(new { success = false, error = "ACTION_NOT_FOUND", actionId }) : Results.Json(r);
});

app.Run();

static async Task<IResult> QueueAction(HttpContext http, string jobId, string actionType, object body, SkillDiagnosticRepository repo, SkillAuditLog audit, CancellationToken ct)
{
    var key = http.Request.Headers["X-Idempotency-Key"].FirstOrDefault()?.Trim();
    if (string.IsNullOrWhiteSpace(key)) return Results.BadRequest(new { success = false, error = "IDEMPOTENCY_KEY_REQUIRED" });
    if (await repo.GetJobSnapshotAsync(jobId, ct) is null) return Results.NotFound(new { success = false, error = "JOB_NOT_FOUND", jobId });

    var queued = await repo.EnqueueActionAsync(jobId, actionType, body, key, "skill-api", ct);
    await audit.WriteAsync(http, actionType + "_job_queued", long.TryParse(jobId, out var n) ? n : 0, body, 202, ct);
    return Results.Accepted(value: queued);
}

static bool FixedTimeEquals(string expected, string actual)
{
    var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
    var b = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
    return CryptographicOperations.FixedTimeEquals(a, b);
}
