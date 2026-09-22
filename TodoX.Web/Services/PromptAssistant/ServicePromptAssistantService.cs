using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;

namespace TodoX.Web.Services.PromptAssistant;

public interface IServicePromptAssistantService
{
    Task<ServicePromptAssistantWorkspace> LoadWorkspaceAsync(Guid serviceId, CancellationToken ct = default);
    Task<ServicePromptAssistantDto> SaveAssistantAsync(ServicePromptAssistantSaveRequest request, CancellationToken ct = default);
    Task TestConnectionAsync(Guid serviceId, CancellationToken ct = default);
    Task<Guid> SaveTrainingDraftAsync(ServicePromptTrainingSaveRequest request, CurrentUserSession? user, CancellationToken ct = default);
    Task PublishAsync(Guid serviceId, Guid versionId, CancellationToken ct = default);
    Task ArchiveAsync(Guid serviceId, Guid versionId, CancellationToken ct = default);
    Task<ServicePromptGenerationResult> GeneratePromptAsync(
        Guid serviceId,
        string userInput,
        CurrentUserSession? userSession,
        CancellationToken ct = default,
        long? videoProjectId = null);

    Task<IReadOnlyList<ServicePromptGenerationDto>> GetProjectGenerationsAsync(long videoProjectId, CancellationToken ct = default);
    Task<ServicePromptGenerationDto?> GetProjectGenerationAsync(long videoProjectId, Guid generationId, CancellationToken ct = default);
    Task<bool> SetActiveProjectGenerationAsync(long videoProjectId, Guid generationId, CancellationToken ct = default);
    Task<ServicePromptGenerationResult> ImportPromptAsync(
        Guid serviceId,
        long videoProjectId,
        string generatedJson,
        CurrentUserSession? userSession,
        CancellationToken ct = default);
}

public sealed class ServicePromptAssistantService : IServicePromptAssistantService
{
    private readonly ServicePromptAssistantRepository _repository;
    private readonly IServicePromptProviderClient _provider;
    private readonly ServicePromptCompiler _compiler;
    private readonly ServicePromptOutputParser _parser;
    private readonly ServicePromptStructureValidator _validator;
    private readonly ServicePromptAssistantOptions _options;

    public ServicePromptAssistantService(
        ServicePromptAssistantRepository repository,
        IServicePromptProviderClient provider,
        ServicePromptCompiler compiler,
        ServicePromptOutputParser parser,
        ServicePromptStructureValidator validator,
        IOptions<ServicePromptAssistantOptions> options)
    {
        _repository = repository;
        _provider = provider;
        _compiler = compiler;
        _parser = parser;
        _validator = validator;
        _options = options.Value;
    }

    public async Task<ServicePromptAssistantWorkspace> LoadWorkspaceAsync(Guid serviceId, CancellationToken ct = default)
    {
        var assistant = await _repository.GetOrCreateAssistantAsync(serviceId, _options, ct);
        var versions = await _repository.GetTrainingVersionsAsync(assistant.Id, ct);
        var generations = await _repository.GetGenerationsAsync(serviceId, ct);
        return new ServicePromptAssistantWorkspace
        {
            Assistant = assistant,
            TrainingVersions = versions,
            Generations = generations
        };
    }

    public Task<ServicePromptAssistantDto> SaveAssistantAsync(
        ServicePromptAssistantSaveRequest request,
        CancellationToken ct = default)
    {
        ValidateAssistant(request);
        return _repository.SaveAssistantAsync(request, ct);
    }

    public async Task TestConnectionAsync(Guid serviceId, CancellationToken ct = default)
    {
        var assistant = await _repository.GetOrCreateAssistantAsync(serviceId, _options, ct);
        if (string.IsNullOrWhiteSpace(assistant.GommoAgentIdBase))
            throw new ServicePromptDomainException("missing_agent_id_base", "Gommo Agent ID Base is not configured.");
        await _provider.CompleteAsync(new ServicePromptProviderRequest(
            _options.ApiUrl, assistant.ProviderCode, assistant.GommoAgentIdBase, "connection test"), ct);
    }

    public async Task<Guid> SaveTrainingDraftAsync(
        ServicePromptTrainingSaveRequest request,
        CurrentUserSession? user,
        CancellationToken ct = default)
    {
        ValidateTraining(request);
        await _repository.GetOrCreateAssistantAsync(request.ServiceId, _options, ct);
        return await _repository.SaveDraftAsync(request.ServiceId, request, user?.UserId, ct);
    }

    public Task PublishAsync(Guid serviceId, Guid versionId, CancellationToken ct = default)
        => _repository.PublishAsync(serviceId, versionId, ct);

    public Task ArchiveAsync(Guid serviceId, Guid versionId, CancellationToken ct = default)
        => _repository.ArchiveAsync(serviceId, versionId, ct);

    public async Task<ServicePromptGenerationResult> GeneratePromptAsync(
        Guid serviceId,
        string userInput,
        CurrentUserSession? userSession,
        CancellationToken ct = default,
        long? videoProjectId = null)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            throw new ArgumentException("User request is required.", nameof(userInput));
        }

        var started = Stopwatch.GetTimestamp();
        var generationId = Guid.NewGuid();
        var assistant = await _repository.GetOrCreateAssistantAsync(serviceId, _options, ct);
        if (!assistant.Enabled)
        {
            return await PersistAndReturnAsync(
                BuildFailure(generationId, assistant, null, userInput, ServicePromptGenerationStatus.ProviderFailed,
                    "assistant_disabled", "Prompt Assistant is disabled.", started),
                userSession,
                serviceId,
                assistant,
                null,
                videoProjectId,
                ct);
        }

        var promptTokens = (int?)null;
        var errors = new List<ServicePromptValidationError>();
        var completionTokens = (int?)null;
        var totalTokens = (int?)null;
        var firstEventMs = (int?)null;
        var firstContentMs = (int?)null;
        var streamingDurationMs = (int?)null;
        var totalDurationMs = (int?)null;
        decimal? credit = null;
        string? runtimeProvider = null;
        var rawResponses = new List<string>();
        string? generatedJson = null;
        string? errorCode = null;
        string? errorMessage = null;
        var status = ServicePromptGenerationStatus.ProviderFailed;

        try
        {
            var response = await _provider.CompleteAsync(
                    new ServicePromptProviderRequest(
                        _options.ApiUrl,
                        assistant.ProviderCode,
                        assistant.GommoAgentIdBase,
                        userInput),
                    ct);
            rawResponses.Add(response.SanitizedRawResponse);
            promptTokens = response.PromptTokens;
            completionTokens = response.CompletionTokens;
            totalTokens = response.TotalTokens;
            firstEventMs = response.FirstEventMs;
            firstContentMs = response.FirstContentMs;
            totalDurationMs = response.TotalDurationMs;
            streamingDurationMs = response.StreamingDurationMs;
            credit = response.Credit;
            runtimeProvider = response.RuntimeProvider;
            using var parsed = _parser.Parse(response.Content);
            generatedJson = parsed.RootElement.GetRawText();
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                throw new ServicePromptProviderException("generated_json_invalid", "Final prompt must be a JSON object.", response.SanitizedRawResponse);
            if (parsed.RootElement.TryGetProperty("scenes", out var scenes))
            {
                if (scenes.ValueKind != JsonValueKind.Array)
                    throw new ServicePromptProviderException("generated_json_invalid", "The scenes field must be an array.", response.SanitizedRawResponse);
                ValidateNumericConsistency(parsed.RootElement, scenes, response.SanitizedRawResponse);
            }
            status = ServicePromptGenerationStatus.Success;
        }
        catch (ServicePromptProviderException ex)
        {
            status = ServicePromptGenerationStatus.ProviderFailed;
            errorCode = ex.Code;
            errorMessage = ex.Message;
            errors.Add(new("$", ex.Code, ex.Message));
            rawResponses.Add(ex.SanitizedResponse);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            status = ServicePromptGenerationStatus.ProviderFailed;
            errorCode = "provider_error";
            errorMessage = ex is InvalidOperationException
                ? ex.Message
                : "Prompt provider request failed.";
        }

        var result = new ServicePromptGenerationResult
        {
            GenerationId = generationId,
            GeneratedJson = generatedJson,
            ValidationPassed = status == ServicePromptGenerationStatus.Success,
            ValidationErrors = errors,
            RepairAttemptCount = 0,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            ProviderCode = assistant.ProviderCode,
            ModelCode = assistant.ModelCode,
            Status = status,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Latency = Stopwatch.GetElapsedTime(started),
            FirstEventMs = firstEventMs,
            FirstContentMs = firstContentMs,
            TotalDurationMs = totalDurationMs,
            StreamingDurationMs = streamingDurationMs,
            Credit = credit,
            RuntimeProvider = runtimeProvider ?? assistant.ProviderCode
        };
        return await PersistAndReturnAsync(
            result,
            userSession,
            serviceId,
            assistant,
            null,
            videoProjectId,
            ct,
            userInput,
            string.Join(Environment.NewLine, rawResponses),
            JsonSerializer.Serialize(new { assistant.ProviderCode, assistant.ModelCode }, ServicePromptJson.Options));
    }

    private async Task<ServicePromptGenerationResult> PersistAndReturnAsync(
        ServicePromptGenerationResult result,
        CurrentUserSession? userSession,
        Guid serviceId,
        ServicePromptAssistantDto assistant,
        ServicePromptTrainingVersionDto? version,
        long? videoProjectId,
        CancellationToken ct,
        string? userInput = null,
        string? rawResponse = null,
        string? requestSnapshot = null)
    {
        var persistence = new ServicePromptGenerationPersistence
            {
                Id = result.GenerationId,
                ServiceId = serviceId,
                AssistantId = assistant.Id,
                TrainingVersionId = version?.Id,
                VideoProjectId = videoProjectId,
                UserId = userSession?.UserId,
                CustomerId = userSession?.CustomerId,
                ProviderCode = result.ProviderCode,
                ModelCode = result.ModelCode,
                UserInput = userInput ?? string.Empty,
                RequestSnapshot = requestSnapshot ?? "{}",
                RawResponse = rawResponse,
                GeneratedJson = result.GeneratedJson,
                ValidationStatus = result.ValidationPassed ? "PASS" : "FAIL",
                ValidationErrors = JsonSerializer.Serialize(result.ValidationErrors, ServicePromptJson.Options),
                RepairAttempts = result.RepairAttemptCount,
                PromptTokens = result.PromptTokens,
                CompletionTokens = result.CompletionTokens,
                TotalTokens = result.TotalTokens,
                RuntimeProvider = result.RuntimeProvider,
                Credit = result.Credit,
                FirstEventMs = result.FirstEventMs,
                FirstContentMs = result.FirstContentMs,
                TotalDurationMs = result.TotalDurationMs,
                StreamingDurationMs = result.StreamingDurationMs,
                Status = result.Status.ToString(),
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage,
                CreatedAt = DateTime.UtcNow - result.Latency,
                CompletedAt = DateTime.UtcNow
            };
        if (videoProjectId is not null && result.ValidationPassed)
        {
            await _repository.SaveGenerationAndSetActiveAsync(persistence, ct);
        }
        else
        {
            await _repository.SaveGenerationAsync(persistence, ct);
        }
        return result;
    }

    public Task<IReadOnlyList<ServicePromptGenerationDto>> GetProjectGenerationsAsync(long videoProjectId, CancellationToken ct = default)
        => _repository.GetProjectGenerationsAsync(videoProjectId, ct);

    public Task<bool> SetActiveProjectGenerationAsync(long videoProjectId, Guid generationId, CancellationToken ct = default)
        => _repository.SetActiveProjectGenerationAsync(videoProjectId, generationId, ct);

    public Task<ServicePromptGenerationDto?> GetProjectGenerationAsync(long videoProjectId, Guid generationId, CancellationToken ct = default)
        => _repository.GetProjectGenerationAsync(videoProjectId, generationId, ct);

    public async Task<ServicePromptGenerationResult> ImportPromptAsync(
        Guid serviceId,
        long videoProjectId,
        string generatedJson,
        CurrentUserSession? userSession,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(generatedJson))
            throw new ArgumentException("Prompt JSON is required.", nameof(generatedJson));

        using var document = JsonDocument.Parse(generatedJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ServicePromptDomainException("generated_json_invalid", "Prompt JSON must be an object.");

        var assistant = await _repository.GetOrCreateAssistantAsync(serviceId, _options, ct);
        var generationId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var result = new ServicePromptGenerationResult
        {
            GenerationId = generationId,
            GeneratedJson = document.RootElement.GetRawText(),
            ValidationPassed = true,
            ProviderCode = "IMPORT",
            ModelCode = string.Empty,
            Status = ServicePromptGenerationStatus.Success,
            Latency = TimeSpan.Zero,
            RuntimeProvider = "IMPORT"
        };
        await _repository.SaveGenerationAndSetActiveAsync(new ServicePromptGenerationPersistence
        {
            Id = generationId,
            ServiceId = serviceId,
            AssistantId = assistant.Id,
            VideoProjectId = videoProjectId,
            UserId = userSession?.UserId,
            CustomerId = userSession?.CustomerId,
            ProviderCode = "IMPORT",
            ModelCode = string.Empty,
            UserInput = "Imported JSON prompt",
            RequestSnapshot = JsonSerializer.Serialize(new { source = "IMPORT" }, ServicePromptJson.Options),
            RawResponse = null,
            GeneratedJson = result.GeneratedJson,
            ValidationStatus = "PASS",
            ValidationErrors = "[]",
            Status = result.Status.ToString(),
            CreatedAt = now,
            CompletedAt = now
        }, ct);
        return result;
    }

    private static ServicePromptGenerationResult BuildFailure(
        Guid generationId,
        ServicePromptAssistantDto assistant,
        ServicePromptTrainingVersionDto? version,
        string userInput,
        ServicePromptGenerationStatus status,
        string code,
        string message,
        long started)
        => new()
        {
            GenerationId = generationId,
            ProviderCode = assistant.ProviderCode,
            ModelCode = assistant.ModelCode,
            Status = status,
            ErrorCode = code,
            ErrorMessage = message,
            ValidationPassed = false,
            ValidationErrors = [new("$", code, message)],
            Latency = Stopwatch.GetElapsedTime(started)
        };

    private static void ValidateAssistant(ServicePromptAssistantSaveRequest request)
    {
        if (request.ServiceId == Guid.Empty) throw new ArgumentException("Service is required.");
        if (string.IsNullOrWhiteSpace(request.ProviderCode)) throw new ArgumentException("Provider is required.");
        if (request.GommoAgentId is <= 0) throw new ArgumentException("Gommo Agent ID must be positive.");
        if (string.IsNullOrWhiteSpace(request.GommoAgentIdBase)) throw new ArgumentException("Gommo Agent ID Base is required.");
        if (request.Temperature is < 0 or > 2) throw new ArgumentException("Temperature must be between 0 and 2.");
        if (request.MaxTokens is <= 0) throw new ArgumentException("Max tokens must be positive.");
    }

    private void ValidateTraining(ServicePromptTrainingSaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.VersionName))
        {
            throw new ArgumentException("Version name is required.");
        }
        if (string.IsNullOrWhiteSpace(request.TemplateJson))
        {
            throw new ArgumentException("Template is required.");
        }
        if (System.Text.Encoding.UTF8.GetByteCount(request.TemplateJson) > _options.TemplateLimit)
        {
            throw new ArgumentException("Template exceeds the maximum allowed size.");
        }
        using var template = JsonDocument.Parse(request.TemplateJson);
        if (template.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            throw new ArgumentException("Template root must be a JSON object or array.");
        }
        if (string.IsNullOrWhiteSpace(request.TemplateFileName)
            || !string.Equals(Path.GetExtension(request.TemplateFileName), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Template file must use the .json extension.");
        }
        if (string.IsNullOrWhiteSpace(request.DescriptionContent))
        {
            throw new ArgumentException("Structure description is required.");
        }
        if (System.Text.Encoding.UTF8.GetByteCount(request.DescriptionContent) > _options.DescriptionLimit)
        {
            throw new ArgumentException("Structure description exceeds the maximum allowed size.");
        }
        var descriptionExtension = Path.GetExtension(request.DescriptionFileName);
        if (!new[] { ".txt", ".md", ".json" }.Contains(descriptionExtension, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Description file must use .txt, .md, or .json.");
        }
    }

    private static void ValidateNumericConsistency(JsonElement root, JsonElement scenes, string raw)
    {
        if (root.TryGetProperty("scene_count", out var sceneCount) && sceneCount.TryGetInt32(out var expectedCount)
            && expectedCount != scenes.GetArrayLength())
            throw new ServicePromptProviderException("generated_json_invalid", "scene_count does not match scenes length.", raw);

        var durations = scenes.EnumerateArray().Select(x => x.TryGetProperty("duration_seconds", out var value) && value.TryGetInt32(out var number) ? number : (int?)null).ToList();
        if (root.TryGetProperty("duration", out var duration) && duration.TryGetInt32(out var expectedDuration) && durations.All(x => x.HasValue)
            && expectedDuration != durations.Sum(x => x!.Value))
            throw new ServicePromptProviderException("generated_json_invalid", "duration does not match scene durations.", raw);

        var shots = scenes.EnumerateArray().Select(x => x.TryGetProperty("shot_count", out var value) && value.TryGetInt32(out var number) ? number : (int?)null).ToList();
        if (root.TryGetProperty("total_shot_count", out var totalShots) && totalShots.TryGetInt32(out var expectedShots) && shots.All(x => x.HasValue)
            && expectedShots != shots.Sum(x => x!.Value))
            throw new ServicePromptProviderException("generated_json_invalid", "total_shot_count does not match scene shot counts.", raw);
    }
    private static int? Sum(int? left, int? right)
        => left is null && right is null ? null : (left ?? 0) + (right ?? 0);
}
