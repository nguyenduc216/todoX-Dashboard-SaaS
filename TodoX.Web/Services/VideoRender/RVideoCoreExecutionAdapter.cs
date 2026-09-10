using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.Platform;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

/// <summary>
/// Bridges the canonical Core RVIDEO job to the existing RVIDEO batch/scene workers.
/// The adapter only creates the batch envelope; provider submission remains owned by the
/// existing SceneVideoRenderHandler and SceneVideoWorkerHandler pipeline.
/// </summary>
public sealed class RVideoCoreExecutionAdapter : ICoreJobExecutionAdapter
{
    public const string RVideoServiceCode = "RVIDEO";
    public const string AdapterCode = "rvideo_render_video_job";
    public const string ExecutionSystem = "render";

    private readonly VideoRenderRepository _projects;
    private readonly RVideoJobSettingsRepository _settings;
    private readonly IRVideoTrustedPayerContextService _payers;
    private readonly IRenderJobService _jobs;

    public RVideoCoreExecutionAdapter(
        VideoRenderRepository projects,
        RVideoJobSettingsRepository settings,
        IRVideoTrustedPayerContextService payers,
        IRenderJobService jobs)
    {
        _projects = projects;
        _settings = settings;
        _payers = payers;
        _jobs = jobs;
    }

    public string ServiceCode => RVideoServiceCode;

    public async Task<CoreExecutionResult> DispatchAsync(
        CoreJobDispatchContext context,
        CancellationToken ct = default)
    {
        if (!string.Equals(context.ServiceCode, RVideoServiceCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"RVIDEO adapter cannot handle service '{context.ServiceCode}'.");
        }

        var request = ParseRequest(context.Input);
        var project = await _projects.GetProjectAsync(request.ProjectId, ct)
            ?? throw new InvalidOperationException("RVIDEO Core job project was not found.");
        var settings = await _settings.GetAsync(project.Id, ct);
        var sceneIds = request.SceneIds.Count == 0
            ? project.Scenes.Select(scene => scene.Id).ToArray()
            : request.SceneIds.Distinct().ToArray();
        var existingSceneIds = project.Scenes.Select(scene => scene.Id).ToHashSet();
        sceneIds = sceneIds.Where(existingSceneIds.Contains).ToArray();
        if (sceneIds.Length == 0)
        {
            throw new InvalidOperationException("RVIDEO Core job has no valid sceneIds.");
        }

        await _jobs.AddEventAsync(
            context.CoreJobId,
            "CORE_EXECUTION_STARTED",
            "Core RVIDEO execution started.",
            new
            {
                coreJobId = context.CoreJobId,
                projectId = project.Id,
                sceneIds,
                jobType = RenderJobTypes.CoreService,
                service = RVideoServiceCode
            },
            "info",
            ct);

        var payer = await _payers.BuildRVideoTrustedPayerContextAsync(project.Id, sceneIds[0], ct);
        var input = new SceneVideoRenderInput
        {
            ProjectId = project.Id,
            SceneIds = sceneIds,
            AspectRatio = request.AspectRatio ?? RVideoRules.ResolveRenderSettings(project.OriginalPrompt).AspectRatio,
            Resolution = request.Resolution ?? RVideoRules.ResolveRenderSettings(project.OriginalPrompt).Resolution,
            UserId = context.RequestContext.UserId ?? project.UserId,
            CustomerId = context.RequestContext.CustomerId ?? project.CustomerId,
            TrustedPayerContext = payer,
            BillingIntent = PointBillingIntent.InitialRender,
            BillingOperationId = null,
            CoreJobId = context.CoreJobId
        };
        if (settings?.UseReferenceImageForAllScenes == true)
        {
            input.ApplySharedReferenceImage(RVideoSceneImageReferenceSelection.Resolve(settings));
        }

        await _jobs.AddEventAsync(
            context.CoreJobId,
            "CORE_RVIDEO_DISPATCH_STARTED",
            "RVIDEO Core execution dispatch started.",
            new
            {
                coreJobId = context.CoreJobId,
                projectId = project.Id,
                sceneIds,
                jobType = RenderJobTypes.CoreService,
                service = RVideoServiceCode
            },
            "info",
            ct);

        var (batchJob, alreadyActive) = await _jobs.EnqueueForProjectIfNoneActiveAsync(
            new RenderJobCreateModel
            {
                JobType = SceneVideoRenderHandler.JobTypeName,
                UserId = input.UserId,
                CustomerId = input.CustomerId,
                Input = input,
                Prompt = new
                {
                    coreJobId = context.CoreJobId,
                    projectId = project.Id,
                    sceneIds,
                    source = "core_service"
                },
                References = Array.Empty<object>(),
                LogCode = $"rvideo-core:{context.CoreJobId:N}",
                ProviderCode = RVideoVideoModelPolicy.ProviderCode,
                ModelCode = RVideoVideoModelPolicy.GetInitial().Model,
                MaxAttempts = 1,
                PointCostEstimate = 0,
                PointStatus = RenderPointStatuses.Pending
            },
            project.Id,
            ct);

        await _jobs.AddEventAsync(
            context.CoreJobId,
            "CORE_RVIDEO_BATCH_CREATED",
            alreadyActive
                ? "Existing RVIDEO batch render job reused for Core execution."
                : "RVIDEO batch render job created for Core execution.",
            new
            {
                coreJobId = context.CoreJobId,
                projectId = project.Id,
                sceneIds,
                childJobId = batchJob.Id,
                alreadyActive,
                jobType = RenderJobTypes.RenderVideoBatch,
                service = RVideoServiceCode
            },
            "info",
            ct);

        return CoreExecutionResult.Deferred(
            ExecutionSystem,
            batchJob.Id.ToString(),
            AdapterCode,
            "RVIDEO batch execution started.",
            JsonSerializer.SerializeToElement(new
            {
                core_job_id = context.CoreJobId,
                project_id = project.Id,
                batch_job_id = batchJob.Id,
                scene_ids = sceneIds,
                already_active = alreadyActive,
                service_code = RVideoServiceCode
            }));
    }

    internal static RVideoCoreExecutionRequest ParseRequest(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("RVIDEO Core input must be a JSON object.");
        }

        var payload = ResolvePayload(input);
        var projectId = ReadLong(payload, "projectId", "project_id")
            ?? throw new InvalidOperationException("RVIDEO Core input is missing projectId.");
        var sceneIds = ReadLongArray(payload, "sceneIds", "scene_ids");
        return new(
            projectId,
            sceneIds,
            ReadString(payload, "aspectRatio", "aspect_ratio"),
            ReadString(payload, "resolution"));
    }

    private static JsonElement ResolvePayload(JsonElement input)
    {
        if (ReadLong(input, "projectId", "project_id").HasValue)
        {
            return input;
        }

        if (!input.TryGetProperty("prompt", out var prompt))
        {
            return input;
        }

        if (prompt.ValueKind == JsonValueKind.Object)
        {
            return prompt;
        }

        if (prompt.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(prompt.GetString()))
        {
            try
            {
                using var document = JsonDocument.Parse(prompt.GetString()!);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return input;
            }
        }

        return input;
    }

    private static string? ReadString(JsonElement input, params string[] names)
        => input.EnumerateObject()
            .Where(property => names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            .Select(property => property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static long? ReadLong(JsonElement input, params string[] names)
    {
        foreach (var property in input.EnumerateObject())
        {
            if (!names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (property.Value.TryGetInt64(out var number))
            {
                return number;
            }

            if (property.Value.ValueKind == JsonValueKind.String
                && long.TryParse(property.Value.GetString(), out number))
            {
                return number;
            }
        }

        return null;
    }

    private static IReadOnlyList<long> ReadLongArray(JsonElement input, params string[] names)
    {
        foreach (var property in input.EnumerateObject())
        {
            if (!names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                || property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            return property.Value.EnumerateArray()
                .Select(value => value.TryGetInt64(out var number)
                    ? number
                    : long.TryParse(value.GetString(), out var parsed) ? parsed : 0)
                .Where(value => value > 0)
                .ToArray();
        }

        return Array.Empty<long>();
    }
}

public sealed record RVideoCoreExecutionRequest(
    long ProjectId,
    IReadOnlyList<long> SceneIds,
    string? AspectRatio,
    string? Resolution);
