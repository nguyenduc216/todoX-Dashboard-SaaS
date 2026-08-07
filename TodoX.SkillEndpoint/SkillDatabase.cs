using System.Text.Json;
using Dapper;
using Npgsql;

namespace TodoX.SkillEndpoint;

public sealed class SkillDatabase
{
    private readonly IConfiguration _configuration;
    public SkillDatabase(IConfiguration configuration) => _configuration = configuration;

    public NpgsqlConnection CreateConnection()
    {
        var cs = _configuration.GetConnectionString("TodoX") ?? Environment.GetEnvironmentVariable("TODOX_SKILL_PG_CONNECTION");
        if (!string.IsNullOrWhiteSpace(cs)) return new NpgsqlConnection(cs);

        var host = Environment.GetEnvironmentVariable("TODOX_PGHOST") ?? "127.0.0.1";
        var port = Environment.GetEnvironmentVariable("TODOX_PGPORT") ?? "5432";
        var db = Environment.GetEnvironmentVariable("TODOX_PGDATABASE") ?? "todox";
        var user = Environment.GetEnvironmentVariable("TODOX_PGUSER") ?? "postgres";
        var password = Environment.GetEnvironmentVariable("TODOX_PGPASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Missing PostgreSQL configuration. Set ConnectionStrings:TodoX or TODOX_PGPASSWORD.");

        return new NpgsqlConnection($"Host={host};Port={port};Database={db};Username={user};Password={password};Pooling=true;Maximum Pool Size=20;Timeout=10;Command Timeout=30");
    }
}

public sealed class SkillDiagnosticRepository
{
    private readonly SkillDatabase _db;
    public SkillDiagnosticRepository(SkillDatabase db) => _db = db;

    public async Task<object?> GetJobSnapshotAsync(string jobId, CancellationToken ct)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);

        // Current /rvideo pipeline uses todox_video_prompt_jobs + todox_video_prompt_scenes.
        if (await TableExistsAsync(cn, "todox_video_prompt_jobs", ct))
        {
            var videoJob = await cn.QueryFirstOrDefaultAsync(new CommandDefinition(
                "select * from public.todox_video_prompt_jobs where id::text=@jobId limit 1",
                new { jobId }, cancellationToken: ct));
            if (videoJob is not null)
                return await BuildVideoPromptSnapshotAsync(cn, jobId, videoJob, ct);
        }

        // Foundation/API-first fallback.
        if (await TableExistsAsync(cn, "todox_jobs", ct))
        {
            var foundationJob = await cn.QueryFirstOrDefaultAsync(new CommandDefinition(@"
select * from public.todox_jobs
where job_id::text=@jobId or id::text=@jobId
order by case when job_id::text=@jobId then 0 else 1 end
limit 1;", new { jobId }, cancellationToken: ct));
            if (foundationJob is not null)
                return await BuildFoundationSnapshotAsync(cn, jobId, foundationJob, ct);
        }

        return null;
    }

    private static async Task<object> BuildVideoPromptSnapshotAsync(NpgsqlConnection cn, string requestedJobId, dynamic job, CancellationToken ct)
    {
        var canonicalJobId = GetText(job, "id") ?? requestedJobId;
        var scenes = await cn.QueryAsync(new CommandDefinition(@"
select * from public.todox_video_prompt_scenes
where job_id::text=@jobId
order by scene_index nulls last, id;", new { jobId = canonicalJobId }, cancellationToken: ct));

        var logs = await ReadRowsIfTableExistsAsync(cn, "todox_job_logs", canonicalJobId, ct,
            "select * from public.todox_job_logs where job_id::text=@jobId order by id desc limit 200");
        var actions = await ReadRowsIfTableExistsAsync(cn, "todox_skill_actions", canonicalJobId, ct,
            "select * from public.todox_skill_actions where job_id::text=@jobId order by created_at desc limit 100");

        return new
        {
            success = true,
            jobFamily = "rvideo",
            requestedJobId,
            canonicalJobId,
            job,
            scenes,
            renderTasks = scenes, // rvideo stores provider/render state directly on scene rows
            steps = Array.Empty<object>(),
            queue = Array.Empty<object>(),
            logs,
            actions,
            readAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static async Task<object> BuildFoundationSnapshotAsync(NpgsqlConnection cn, string requestedJobId, dynamic job, CancellationToken ct)
    {
        var canonicalJobId = GetText(job, "job_id") ?? GetText(job, "id") ?? requestedJobId;
        var scenes = await ReadRowsIfTableExistsAsync(cn, "todox_scenes", canonicalJobId, ct,
            "select * from public.todox_scenes where job_id::text=@jobId order by scene_index nulls last, id::text");
        var renderTasks = await ReadRowsIfTableExistsAsync(cn, "todox_scene_render_tasks", canonicalJobId, ct,
            "select * from public.todox_scene_render_tasks where job_id::text=@jobId order by scene_index nulls last, id");
        var steps = await ReadRowsIfTableExistsAsync(cn, "todox_job_steps", canonicalJobId, ct,
            "select * from public.todox_job_steps where job_id::text=@jobId order by id");
        var queue = await ReadRowsIfTableExistsAsync(cn, "todox_queue", canonicalJobId, ct,
            "select * from public.todox_queue where job_id::text=@jobId order by id desc limit 100");
        var logs = await ReadRowsIfTableExistsAsync(cn, "todox_job_logs", canonicalJobId, ct,
            "select * from public.todox_job_logs where job_id::text=@jobId order by id desc limit 200");
        var actions = await ReadRowsIfTableExistsAsync(cn, "todox_skill_actions", canonicalJobId, ct,
            "select * from public.todox_skill_actions where job_id::text=@jobId order by created_at desc limit 100");

        return new { success = true, jobFamily = "foundation", requestedJobId, canonicalJobId, job, scenes, renderTasks, steps, queue, logs, actions, readAtUtc = DateTimeOffset.UtcNow };
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
        var providerPending = new List<int>();

        foreach (var task in tasks.EnumerateArray())
        {
            var sceneIndex = TryGetInt(task, "scene_index");
            var status = (TryGetString(task, "status") ?? "").Trim().ToUpperInvariant();
            var errorText = (TryGetString(task, "error_message") ?? TryGetString(task, "error") ?? task.GetRawText());
            var renderTaskId = TryGetString(task, "render_task_id");
            var providerTaskId = TryGetString(task, "provider_task_id");
            var videoUrl = TryGetString(task, "video_url") ?? TryGetString(task, "video_file_url") ?? TryGetString(task, "download_url");

            var timeoutPending = status == "TIMEOUT_PENDING" || errorText.Contains("TIMEOUT_PENDING", StringComparison.OrdinalIgnoreCase);
            var processingWithTask = status == "VIDEO_PROCESSING" && (!string.IsNullOrWhiteSpace(renderTaskId) || !string.IsNullOrWhiteSpace(providerTaskId));
            var terminalFailed = status is "FAILED" or "ERROR" or "VIDEO_FAILED" or "CANCELLED";

            if (sceneIndex is not null && timeoutPending) providerPending.Add(sceneIndex.Value);
            if (sceneIndex is not null && (terminalFailed || timeoutPending)) retryable.Add(sceneIndex.Value);

            if (timeoutPending)
            {
                findings.Add(new
                {
                    code = "SCENE_TIMEOUT_PENDING",
                    severity = "warning",
                    sceneIndex,
                    status,
                    renderTaskId,
                    providerTaskId,
                    recommendation = "Query 79AI/provider first. Do not submit a new video task while provider may still be processing."
                });
            }
            else if (terminalFailed)
            {
                findings.Add(new { code = "SCENE_RENDER_FAILED", severity = "error", sceneIndex, status, renderTaskId, providerTaskId, retryable = true });
            }
            else if (processingWithTask)
            {
                findings.Add(new { code = "SCENE_PROVIDER_TASK_IN_PROGRESS", severity = "info", sceneIndex, status, renderTaskId, providerTaskId });
            }
            else if (status.Contains("COMPLETE") && string.IsNullOrWhiteSpace(videoUrl))
            {
                findings.Add(new { code = "SCENE_COMPLETED_WITHOUT_VIDEO_URL", severity = "error", sceneIndex, status });
            }
        }

        var jobStatus = TryGetString(job, "status");
        var distinctRetryable = retryable.Distinct().OrderBy(x => x).ToArray();
        if (distinctRetryable.Length > 0 && (jobStatus?.Contains("COMPLETE", StringComparison.OrdinalIgnoreCase) == true || jobStatus?.Contains("DONE", StringComparison.OrdinalIgnoreCase) == true))
            findings.Add(new { code = "JOB_SCENE_STATE_MISMATCH", severity = "error", jobStatus, retryableScenes = distinctRetryable });

        return new
        {
            success = true,
            requestedJobId = jobId,
            jobFamily = json.GetProperty("jobFamily").GetString(),
            jobStatus,
            retryableSceneIndexes = distinctRetryable,
            providerPendingSceneIndexes = providerPending.Distinct().OrderBy(x => x).ToArray(),
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
        var pending = json.GetProperty("providerPendingSceneIndexes").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var selected = request.SceneIndexes is { Length: > 0 } ? retryable.Intersect(request.SceneIndexes).OrderBy(x => x).ToArray() : retryable;

        var actions = new List<string>();
        if (request.IncludeProviderLookup && (selected.Length > 0 || pending.Length > 0)) actions.Add("RECONCILE_PROVIDER_TASK");
        if (selected.Except(pending).Any()) actions.Add("RESET_FAILED_SCENE_TO_QUEUED");
        if (selected.Length > 0) actions.Add("REQUEUE_VIDEO_WORKER");
        actions.Add("REBUILD_JOB_SUMMARY");
        if (request.IncludeBillingCheck) actions.Add("RECONCILE_BILLING");

        return new
        {
            success = true,
            jobId,
            sceneIndexes = selected,
            providerPendingSceneIndexes = pending,
            actions,
            destructive = false,
            guardrail = "TIMEOUT_PENDING scenes must be reconciled with provider before any new provider submission.",
            diagnostic
        };
    }

    public async Task<object> EnqueueActionAsync(string jobId, string actionType, object request, string idempotencyKey, string actor, CancellationToken ct)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        var existing = await cn.QueryFirstOrDefaultAsync(new CommandDefinition("select * from public.todox_skill_actions where idempotency_key=@key limit 1", new { key = idempotencyKey }, cancellationToken: ct));
        if (existing is not null) return new { success = true, duplicate = true, action = existing };

        var actionId = $"act_{Guid.NewGuid():N}";
        var requestJson = JsonSerializer.Serialize(request);
        var inserted = await cn.QuerySingleAsync(new CommandDefinition(@"
insert into public.todox_skill_actions(action_id,job_id,action_type,status,idempotency_key,request_json,requested_by)
values(@actionId,@jobId,@actionType,'pending',@key,cast(@requestJson as jsonb),@actor)
returning *;", new { actionId, jobId, actionType, key = idempotencyKey, requestJson, actor }, cancellationToken: ct));
        return new { success = true, duplicate = false, action = inserted };
    }

    public async Task<object?> GetActionAsync(string actionId, CancellationToken ct)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        var row = await cn.QueryFirstOrDefaultAsync(new CommandDefinition("select * from public.todox_skill_actions where action_id=@actionId limit 1", new { actionId }, cancellationToken: ct));
        return row is null ? null : new { success = true, action = row };
    }

    private static async Task<IEnumerable<dynamic>> ReadRowsIfTableExistsAsync(NpgsqlConnection cn, string table, string jobId, CancellationToken ct, string sql)
        => await TableExistsAsync(cn, table, ct) ? await cn.QueryAsync(new CommandDefinition(sql, new { jobId }, cancellationToken: ct)) : Array.Empty<dynamic>();

    private static async Task<bool> TableExistsAsync(NpgsqlConnection cn, string table, CancellationToken ct)
        => await cn.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('public.' || @table) is not null", new { table }, cancellationToken: ct));

    private static string? GetText(dynamic row, string key)
        => row is IDictionary<string, object> map && map.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static string? TryGetString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? TryGetInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i)) return i;
        return int.TryParse(value.ToString(), out i) ? i : null;
    }
}
