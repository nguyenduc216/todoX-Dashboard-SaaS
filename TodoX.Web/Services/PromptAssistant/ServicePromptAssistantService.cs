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
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            throw new ArgumentException("User request is required.", nameof(userInput));
        }

        var started = Stopwatch.GetTimestamp();
        var generationId = Guid.NewGuid();
        var assistant = await _repository.GetOrCreateAssistantAsync(serviceId, _options, ct);
        var version = await _repository.GetPublishedAsync(assistant.Id, ct);
        if (!assistant.Enabled)
        {
            return await PersistAndReturnAsync(
                BuildFailure(generationId, assistant, version, userInput, ServicePromptGenerationStatus.ProviderFailed,
                    "assistant_disabled", "Prompt Assistant is disabled.", started),
                userSession,
                serviceId,
                assistant,
                version,
                ct);
        }

        if (version is null)
        {
            return await PersistAndReturnAsync(
                BuildFailure(generationId, assistant, null, userInput, ServicePromptGenerationStatus.ProviderFailed,
                    "no_published_version", "No published training version is available.", started),
                userSession,
                serviceId,
                assistant,
                null,
                ct);
        }

        var templateJson = version.TemplateJson;
        var errors = new List<ServicePromptValidationError>();
        var repairAttempts = 0;
        var promptTokens = (int?)null;
        var completionTokens = (int?)null;
        var totalTokens = (int?)null;
        var rawResponses = new List<string>();
        string? generatedJson = null;
        string? errorCode = null;
        string? errorMessage = null;
        var status = ServicePromptGenerationStatus.ValidationFailed;

        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var response = await _provider.CompleteAsync(
                    new ServicePromptProviderRequest(
                        _options.ApiUrl,
                        assistant.ProviderCode,
                        assistant.GommoAgentIdBase,
                        userInput),
                    ct);
                rawResponses.Add(response.SanitizedRawResponse);
                promptTokens = Sum(promptTokens, response.PromptTokens);
                completionTokens = Sum(completionTokens, response.CompletionTokens);
                totalTokens = Sum(totalTokens, response.TotalTokens);

                try
                {
                    using var parsed = _parser.Parse(response.Content);
                    generatedJson = parsed.RootElement.GetRawText();
                    errors = _validator.Validate(templateJson, generatedJson).ToList();
                }
                catch (ServicePromptProviderException ex)
                {
                    generatedJson = null;
                    errors =
                    [
                        new("$", ex.Code, ex.Message)
                    ];
                    errorCode = ex.Code;
                    errorMessage = ex.Message;
                }

                if (errors.Count == 0)
                {
                    status = ServicePromptGenerationStatus.Success;
                    errorCode = null;
                    errorMessage = null;
                    break;
                }

                if (attempt >= Math.Clamp(assistant.MaxRepairAttempts, 0, 3))
                {
                    status = attempt == 0
                        ? ServicePromptGenerationStatus.ValidationFailed
                        : ServicePromptGenerationStatus.RepairFailed;
                    errorCode ??= "validation_failed";
                    errorMessage ??= "Generated JSON failed structural validation.";
                    break;
                }

                repairAttempts++;
            }
        }
        catch (ServicePromptProviderException ex)
        {
            status = ServicePromptGenerationStatus.ProviderFailed;
            errorCode = ex.Code;
            errorMessage = ex.Message;
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
            RepairAttemptCount = repairAttempts,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            ProviderCode = assistant.ProviderCode,
            ModelCode = assistant.ModelCode,
            Status = status,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Latency = Stopwatch.GetElapsedTime(started)
        };
        return await PersistAndReturnAsync(
            result,
            userSession,
            serviceId,
            assistant,
            version,
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
        CancellationToken ct,
        string? userInput = null,
        string? rawResponse = null,
        string? requestSnapshot = null)
    {
        if (version is null)
        {
            return result;
        }

        await _repository.SaveGenerationAsync(
            new ServicePromptGenerationPersistence
            {
                Id = result.GenerationId,
                ServiceId = serviceId,
                AssistantId = assistant.Id,
                TrainingVersionId = version.Id,
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
                Status = result.Status.ToString(),
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage,
                CreatedAt = DateTime.UtcNow - result.Latency,
                CompletedAt = DateTime.UtcNow
            },
            ct);
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

    private static int? Sum(int? left, int? right)
        => left is null && right is null ? null : (left ?? 0) + (right ?? 0);
}
