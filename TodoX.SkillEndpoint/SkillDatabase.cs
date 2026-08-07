using System.Text.Json;
using Dapper;
using Npgsql;

namespace TodoX.SkillEndpoint;

public sealed class SkillDatabase
{
    private readonly IConfiguration _configuration;

    public SkillDatabase(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public NpgsqlConnection CreateConnection()
    {
        var cs = _configuration.GetConnectionString("TodoX")
                 ?? Environment.GetEnvironmentVariable("TODOX_SKILL_PG_CONNECTION");

        if (string.IsNullOrWhiteSpace(cs))
        {
            var host = Environment.GetEnvironmentVariable("TODOX_PGHOST") ?? "127.0.0.1";
            var port = Environment.GetEnvironmentVariable("TODOX_PGPORT") ?? "5432";
            var db = Environment.GetEnvironmentVariable("TODOX_PGDATABASE") ?? "todox";
            var user = Environment.GetEnvironmentVariable("TODOX_PGUSER") ?? "postgres";
            var password = Environment.GetEnvironmentVariable("TODOX_PGPASSWORD");
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Missing TodoX PostgreSQL configuration. Set ConnectionStrings:TodoX or TODOX_PGPASSWORD.");

            cs = $"Host={host};Port={port};Database={db};Username={user};Password={password};Pooling=true;Maximum Pool Size=20;Timeout=10;Command Timeout=30";
        }

        return new NpgsqlConnection(cs);
    }
}

public sealed class SkillDiagnosticRepository
{
    private readonly SkillDatabase _db;

    public SkillDiagnosticRepository(SkillDatabase db)
    {
        _db = db;
    }

    public async Task<object?> GetJobSnapshotAsync(string jobId, CancellationToken ct)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);

        var job = await FindJobAsync(cn, jobId, ct);
        if (job is null) return null;

        var canonicalJobId = GetText(job, "job_id") ?? GetText(job, "id") ?? jobId;
        var scenes = await ReadRowsIfTableExistsAsync(cn, "todox_scenes", canonicalJobId, ct,
            "select * from todox_scenes where job_id::text=@jobId order by scene_index nulls last, id::text");
        var renderTasks = await ReadRowsIfTableExistsAsync(cn, "todox_scene_render_tasks", canonicalJobId, ct,
            "select * from todox_scene_render_tasks where job_id::text=@jobId order by scene_index nulls last, id");
        var steps = await ReadRowsIfTableExistsAsync(cn, "todox_job_steps", canonicalJobId, ct,
            "select * from todox_job_steps where job_id::text=@jobId order by id");
        var queue = await ReadRowsIfTableExistsAsync(cn, "todox_queue", canonicalJobId, ct,
            "select * from todox_queue where job_id::text=@jobId order by id desc limit 100");
        var logs = await ReadRowsIfTableExistsAsync(cn, "todox_job_logs", canonicalJobId, ct,
            "select * from todox_job_logs where job_id::text=@jobId order by id desc limit 200");
        var actions = await ReadRowsIfTableExistsAsync(cn, "todox_skill_actions", canonicalJobId, ct,
            "select * from todox_skill_actions where job_id::text=@jobId order by created_at desc limit 100");

        return new
        {
            success = true,
            requestedJobId = jobId,
            canonicalJobId,
            job,
            scenes,
            renderTasks,
            steps,
            queue,
            logs,
            actions,
            readAtUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task<object?> DiagnoseAsync(string jobId, CancellationToken ct)
    {
        var snapshot = await GetJobSnapshotAsync(jobId, ct);
        if (snapshot is null) return null;

        var json = JsonSerializer.SerializeToElement(snapshot);
        var job = json.GetProperty("job");
        var tasks = json.GetProperty("renderTasks");
        var findings = new List<object>();
        var retryable = new List<int>();

        foreach (var task in tasks.EnumerateArray())
        {
            var sceneIndex = TryGetInt(task, "scene_index");
            var status = TryGetString(task, "status")?.Trim().ToUpperInvariant();
            var errorText = TryGetString(task, "error") ?? task.GetRawText();

            if (sceneIndex is not null && IsRetryableStatus(status, errorText))
                retryable.Add(sceneIndex.Value);

            if (status is "TIMEOUT_PENDING" or "FAILED" or "ERROR" or "CANCELLED" ||
                errorText.Contains("TIMEOUT_PENDING", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new
                {
                    code = status == "TIMEOUT_PENDING" || errorText.Contains("TIMEOUT_PENDING", StringComparison.OrdinalIgnoreCase)
                        ? "SCENE_TIMEOUT_PENDING"
                        : "SCENE_RENDER_FAILED",
                    severity = "error",
                    sceneIndex,
                    status,
                    retryable = sceneIndex is not null,
                    recommendation = "Reconcile provider task before submitting a new render. Retry only if provider is terminal failed or the task cannot be recovered."
                });
            }
        }

        var jobStatus = TryGetString(job, "status");
        if (retryable.Count > 0 && string.Equals(jobStatus, "completed", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new
            {
                code = "JOB_SCENE_STATE_MISMATCH",
                severity = "error",
                jobStatus,
                retryableScenes = retryable.Distinct().OrderBy(x => x).ToArray(),
                recommendation = "Rebuild job summary after reconciling failed scenes."
            });
        }

        return new
        {
            success = true,
            requestedJobId = jobId,
            jobStatus,
            retryableSceneIndexes = retryable.Distinct().OrderBy(x => x).ToArray(),
            findingCount = findings.Count,
            findings,
            snapshot,
            diagnosedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task<object?> BuildRepairPlanAsync(string jobId, RepairPlanRequest request, CancellationToken ct)
    {
        var diagnostic = await DiagnoseAsync(jobId, ct);
        if (diagnostic is null) return null;

        var json = JsonSerializer.SerializeToElement(diagnostic);
        var retryable = json.GetProperty("retryableSceneIndexes").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var selected = request.SceneIndexes is { Length: > 0 }
            ? retryable.Intersect(request.SceneIndexes).OrderBy(x => x).ToArray()
            : retryable;

        var actions = new List<string>();
        if (request.IncludeProviderLookup && selected.Length > 0) actions.Add("RECONCILE_PROVIDER_TASK");
        if (selected.Length > 0) actions.Add("MARK_FAILED_SCENE_RETRYABLE");
        if (selected.Length > 0) actions.Add("REQUEUE_VIDEO_WORKER");
        actions.Add("REBUILD_JOB_SUMMARY");
        if (request.IncludeBillingCheck) actions.Add("RECONCILE_BILLING");

        return new
        {
            success = true,
            jobId,
            sceneIndexes = selected,
            actions,
            destructive = false,
            note = "This endpoint only creates a plan. Provider reconciliation must occur before a new provider submission.",
            diagnostic
        };
    }

    public async Task<object> EnqueueActionAsync(string jobId, string actionType, object request, string idempotencyKey, string actor, CancellationToken ct)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);

        var existing = await cn.QueryFirstOrDefaultAsync(new CommandDefinition(
            "select * from todox_skill_actions where idempotency_key=@key limit 1",
            new { key = idempotencyKey }, cancellationToken: ct));
        if (existing is not null)
            return new { success = true, duplicate = true, action = existing };

        var actionId = $"act_{Guid.NewGuid():N}";
        var requestJson = JsonSerializer.Serialize(request);
        var inserted = await cn.QuerySingleAsync(new CommandDefinition(@"
insert into todox_skill_actions(action_id,job_id,action_type,status,idempotency_key,request_json,requested_by)
values(@actionId,@jobId,@actionType,'pending',@key,cast(@requestJson as jsonb),@actor)
returning *;", new { actionId, jobId, actionType, key = idempotencyKey, requestJson, actor }, cancellationToken: ct));

        return new { success = true, duplicate = false, action = inserted };
    }

    public async Task<object?> GetActionAsync(string actionId, CancellationToken ct)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        var row = await cn.QueryFirstOrDefaultAsync(new CommandDefinition(
            "select * from todox_skill_actions where action_id=@actionId limit 1", new { actionId }, cancellationToken: ct));
        return row is null ? null : new { success = true, action = row };
    }

    private static async Task<dynamic?> FindJobAsync(NpgsqlConnection cn, string jobId, CancellationToken ct)
    {
        if (!await TableExistsAsync(cn, "todox_jobs", ct)) return null;

        var sql = @"
select * from todox_jobs
where job_id::text=@jobId or id::text=@jobId
order by case when job_id::text=@jobId then 0 else 1 end
limit 1;";
        return await cn.QueryFirstOrDefaultAsync(new CommandDefinition(sql, new { jobId }, cancellationToken: ct));
    }

    private static async Task<IEnumerable<dynamic>> ReadRowsIfTableExistsAsync(NpgsqlConnection cn, string table, string jobId, CancellationToken ct, string sql)
    {
        if (!await TableExistsAsync(cn, table, ct)) return Array.Empty<dynamic>();
        return await cn.QueryAsync(new CommandDefinition(sql, new { jobId }, cancellationToken: ct));
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection cn, string table, CancellationToken ct)
    {
        return await cn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select to_regclass('public.' || @table) is not null", new { table }, cancellationToken: ct));
    }

    private static string? GetText(dynamic row, string key)
    {
        if (row is IDictionary<string, object> map && map.TryGetValue(key, out var value))
            return value?.ToString();
        return null;
    }

    private static string? TryGetString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? TryGetInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i)) return i;
        return int.TryParse(value.ToString(), out i) ? i : null;
    }

    private static bool IsRetryableStatus(string? status, string errorText)
    {
        if (status is "FAILED" or "ERROR" or "TIMEOUT_PENDING" or "CANCELLED") return true;
        return errorText.Contains("TIMEOUT_PENDING", StringComparison.OrdinalIgnoreCase)
               || errorText.Contains("TIMEOUT", StringComparison.OrdinalIgnoreCase)
               || errorText.Contains("FAILED", StringComparison.OrdinalIgnoreCase);
    }
}
