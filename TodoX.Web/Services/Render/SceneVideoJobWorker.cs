using System.Text.Json;
using System.Text.Json.Nodes;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Services.Render;

public sealed class SceneVideoJobWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SceneVideoJobWorker> _logger;

    public SceneVideoJobWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<SceneVideoJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("RenderQueue:Enabled", false))
        {
            _logger.LogInformation("Scene video job worker is disabled because RenderQueue:Enabled=false.");
            return;
        }

        var parallelism = Math.Max(1, _config.GetValue("VideoRender:MaxConcurrentSceneJobs", 3));
        var tasks = Enumerable.Range(0, parallelism)
            .Select(index => RunLaneAsync(index + 1, stoppingToken))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    private async Task RunLaneAsync(int lane, CancellationToken stoppingToken)
    {
        var workerKey = $"{Environment.MachineName}-scene-video-{lane}-{Guid.NewGuid():N}";
        var idleDelay = TimeSpan.FromMilliseconds(Math.Max(250, _config.GetValue("RenderQueue:IdleDelayMs", 1500)));
        var lockFor = TimeSpan.FromMinutes(Math.Max(1, _config.GetValue("RenderQueue:LockMinutes", 15)));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
                await tenant.EnsureLoadedAsync(stoppingToken);

                var jobs = scope.ServiceProvider.GetRequiredService<IRenderJobService>();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IRenderJobDispatcher>();
                var job = await jobs.ClaimNextByJobTypeAsync(workerKey, lockFor, new[] { RenderJobTypes.RenderSceneVideo }, stoppingToken);
                if (job is null)
                {
                    await Task.Delay(idleDelay, stoppingToken);
                    continue;
                }

                try
                {
                    if (job.AttemptCount > job.MaxAttempts)
                    {
                        _logger.LogError(
                            "RENDER_JOB_ATTEMPT_BUDGET_EXCEEDED jobId={JobId} jobType={JobType} attemptCount={AttemptCount} maxAttempts={MaxAttempts}",
                            job.Id,
                            job.JobType,
                            job.AttemptCount,
                            job.MaxAttempts);
                        throw new RenderJobTerminalFailureException("Render job attempt budget exceeded.");
                    }

                    await DispatchClaimAsync(jobs, dispatcher, job, workerKey, stoppingToken);
                    await jobs.MarkStatusAsync(job.Id, RenderJobStatuses.Completed, ct: stoppingToken);
                }
                catch (RenderJobPendingReconciliationException ex)
                {
                    await AddAi79SubmitDiagnosticsAsync(jobs, job, ex, stoppingToken);
                    await jobs.AddEventAsync(job.Id, "JOB_PENDING_RECONCILIATION", ex.Message,
                        BuildJobFailureEventData(job, ex), "warning", stoppingToken);
                    await jobs.MarkStatusAsync(job.Id, RenderJobStatuses.PendingReconciliation, errorCode: ex.GetType().Name, errorMessage: ex.Message, ct: stoppingToken);
                }
                catch (RenderJobDeferredException ex)
                {
                    await jobs.AddEventAsync(job.Id, "JOB_DEFERRED", ex.Message,
                        new { job.AttemptCount, job.MaxAttempts }, "info", stoppingToken);
                }
                catch (InvalidOperationException ex) when (ex.Message is "RVIDEO_JOB_VERSION_OWNERSHIP_REQUIRED" or "RVIDEO_JOB_STATUS_REJECTED" or "RVIDEO_JOB_SCENE_IDENTITY_REQUIRED" or "RVIDEO_JOB_CALLER_OWNERSHIP_LOST")
                {
                    var quarantined = await QuarantineRejectedClaimAsync(jobs, job, workerKey, ex.Message, stoppingToken);
                    _logger.LogWarning("Scene video rejected jobId={JobId} attempt={AttemptCount} reason={Reason} quarantined={Quarantined}; dispatch and retry skipped.", job.Id, job.AttemptCount, ex.Message, quarantined);
                }
                catch (RenderJobTerminalFailureException ex)
                {
                    await SyncTerminalSceneVideoVersionAsync(scope, job, ex, stoppingToken);
                    await AddAi79SubmitDiagnosticsAsync(jobs, job, ex, stoppingToken);
                    await jobs.AddEventAsync(job.Id, "JOB_FAILED", ex.Message,
                        BuildJobFailureEventData(job, ex), "error", stoppingToken);
                    await jobs.MarkStatusAsync(job.Id, RenderJobStatuses.Failed, errorCode: ex.GetType().Name, errorMessage: ex.Message, ct: stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    await jobs.CancelAsync(job.Id, "Scene video worker cancellation requested.", ct: CancellationToken.None);
                }
                catch (Exception ex)
                {
                    if (ex is Npgsql.PostgresException pg)
                    {
                        _logger.LogError(pg,
                            "RVIDEO_SQL_DIAGNOSTIC operation=DispatchClaim sqlState={SqlState} position={Position} internalPosition={InternalPosition} jobId={JobId} sceneId=see_job_snapshot versionId=see_job_logical_request; SQL command omitted to avoid logging snapshot/provider data",
                            pg.SqlState, pg.Position, pg.InternalPosition, job.Id);
                    }
                    await AddAi79SubmitDiagnosticsAsync(jobs, job, ex, stoppingToken);
                    var shouldRetry = job.AttemptCount < job.MaxAttempts;
                    if (shouldRetry)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Max(0, job.AttemptCount)) * 5));
                        await jobs.ScheduleRetryAsync(job.Id, delay, ex.GetType().Name, ex.Message, stoppingToken);
                    }
                    else
                    {
                        await SyncTerminalSceneVideoVersionAsync(scope, job, ex, stoppingToken);
                        await jobs.AddEventAsync(job.Id, "JOB_FAILED", ex.Message,
                            BuildJobFailureEventData(job, ex), "error", stoppingToken);
                        await jobs.MarkStatusAsync(job.Id, RenderJobStatuses.Failed, errorCode: ex.GetType().Name, errorMessage: ex.Message, ct: stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scene video job worker lane {Lane} failed.", lane);
                await Task.Delay(idleDelay, stoppingToken);
            }
        }
    }

    internal static async Task DispatchClaimAsync(IRenderJobService jobs, IRenderJobDispatcher dispatcher,
        RenderJobDto job, string workerKey, CancellationToken ct)
    {
        await jobs.MarkSceneVideoClaimRenderingAsync(job, workerKey, ct);
        await dispatcher.DispatchAsync(job, ct);
    }

    internal static Task<bool> QuarantineRejectedClaimAsync(IRenderJobService jobs, RenderJobDto job,
        string workerKey, string reason, CancellationToken ct)
        => jobs.QuarantineRejectedSceneVideoClaimAsync(job, workerKey, reason, ct);

    private static async Task AddAi79SubmitDiagnosticsAsync(
        IRenderJobService jobs,
        RenderJobDto job,
        Exception exception,
        CancellationToken ct)
    {
        var diagnostics = BuildAi79SubmitDiagnostics(job, exception);
        if (diagnostics is not null)
        {
            await jobs.AddEventAsync(
                job.Id,
                "RVIDEO_79AI_SUBMIT_DIAGNOSTICS",
                "RVideo 79AI submit diagnostics captured.",
                diagnostics,
                "error",
                ct);
        }
    }

    private static object BuildJobFailureEventData(RenderJobDto job, Exception exception)
        => BuildAi79SubmitDiagnostics(job, exception)
           ?? new
           {
               exceptionType = exception.GetType().Name,
               attemptCount = job.AttemptCount,
               maxAttempts = job.MaxAttempts
           };

    private static object? BuildAi79SubmitDiagnostics(RenderJobDto job, Exception exception)
    {
        var ai79 = FindAi79SubmitException(exception);
        if (ai79 is null)
        {
            return null;
        }

        return new
        {
            exceptionType = nameof(Ai79TaskSubmitException),
            provider = "79ai",
            model = job.ModelCode,
            httpStatusCode = (int?)ai79.HttpStatusCode,
            providerErrorCode = ai79.ErrorCode,
            sanitizedResponseJson = SanitizeDiagnosticJson(ai79.SanitizedResponseJson),
            sanitizedRequestMetadataJson = SanitizeDiagnosticJson(ai79.SanitizedRequestMetadataJson),
            attemptCount = job.AttemptCount,
            maxAttempts = job.MaxAttempts
        };
    }

    private static Ai79TaskSubmitException? FindAi79SubmitException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is Ai79TaskSubmitException ai79)
            {
                return ai79;
            }
        }

        return null;
    }

    private static string SanitizeDiagnosticJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return JsonSerializer.Serialize(string.Empty, JsonOptions);
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            var sanitized = SanitizeDiagnosticElement(document.RootElement);
            return sanitized?.ToJsonString(JsonOptions) ?? JsonSerializer.Serialize(string.Empty, JsonOptions);
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(string.Empty, JsonOptions);
        }
    }

    private static JsonNode? SanitizeDiagnosticElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => SanitizeDiagnosticObject(element),
            JsonValueKind.Array => SanitizeDiagnosticArray(element),
            JsonValueKind.String => JsonValue.Create(element.GetString()),
            JsonValueKind.Number => JsonValue.Create(element.GetRawText()),
            JsonValueKind.True => JsonValue.Create(true),
            JsonValueKind.False => JsonValue.Create(false),
            _ => null
        };
    }

    private static JsonObject SanitizeDiagnosticObject(JsonElement element)
    {
        var result = new JsonObject();
        foreach (var property in element.EnumerateObject())
        {
            if (IsSensitiveDiagnosticProperty(property.Name))
            {
                continue;
            }

            result[property.Name] = SanitizeDiagnosticElement(property.Value);
        }

        return result;
    }

    private static JsonArray SanitizeDiagnosticArray(JsonElement element)
    {
        var result = new JsonArray();
        foreach (var item in element.EnumerateArray())
        {
            result.Add(SanitizeDiagnosticElement(item));
        }

        return result;
    }

    private static bool IsSensitiveDiagnosticProperty(string name)
        => name.Contains("token", StringComparison.OrdinalIgnoreCase)
           || name.Contains("authorization", StringComparison.OrdinalIgnoreCase)
           || name.Contains("credential", StringComparison.OrdinalIgnoreCase)
           || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
           || name.Contains("password", StringComparison.OrdinalIgnoreCase)
           || name.Contains("api_key", StringComparison.OrdinalIgnoreCase)
           || name.Contains("apikey", StringComparison.OrdinalIgnoreCase);

    private static async Task SyncTerminalSceneVideoVersionAsync(IServiceScope scope, RenderJobDto job, Exception failure, CancellationToken ct)
    {
        var versions = scope.ServiceProvider.GetRequiredService<ISceneMediaVersioningService>();
        var recovery = scope.ServiceProvider.GetRequiredService<IRVideoSceneVideoRecoveryService>();
        var repository = scope.ServiceProvider.GetRequiredService<VideoRenderRepository>();
        var input = JsonSerializer.Deserialize<SceneVideoRenderWorkItemInput>(job.InputJson, JsonOptions);
        if (input is null || string.IsNullOrWhiteSpace(input.LogicalRequestId))
        {
            return;
        }

        var version = await versions.GetSceneVideoVersionByLogicalRequestIdAsync(input.LogicalRequestId, ct);
        if (version is null || version.ProviderTaskId is not null && !string.IsNullOrWhiteSpace(version.ProviderTaskId))
        {
            return;
        }

        var project = await repository.GetProjectAsync(input.ProjectId, ct);
        var scene = project?.Scenes.FirstOrDefault(x => x.Id == input.SceneId);
        if (scene is not null)
        {
            await recovery.RecoverStuckAsync(input.ProjectId, scene, version, job, failure.Message, ct);
        }
        else
        {
            await versions.FailSceneVideoVersionAsync(version.Id, failure.GetType().Name, failure.Message, ct);
        }
    }
}
