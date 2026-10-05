using TodoX.Web.Models;
using TodoX.Web.Services.PromptAssistant;

namespace TodoX.Web.Services.VideoRender;

public interface IRVideoPromptGenerationAction
{
    Task<RVideoPromptGenerationOutcome> GenerateAsync(RVideoPromptGenerationRequest request, CancellationToken ct = default);
}

public sealed record RVideoPromptGenerationRequest(
    Guid ServiceId,
    string UserInput,
    CurrentUserSession? UserSession,
    long? VideoProjectId = null,
    PromptAssistantCharacterReference? CharacterReference = null,
    int? DurationSeconds = null,
    bool CreativeMode = false);

public sealed record RVideoPromptGenerationOutcome(
    ServicePromptGenerationResult Result,
    TodoXVideoPromptParseResult ParseResult);
public sealed class RVideoPromptGenerationAction : IRVideoPromptGenerationAction
{
    private readonly IServicePromptAssistantService _assistant;
    private readonly ITodoXVideoPromptParser _parser;
    private readonly ILogger<RVideoPromptGenerationAction> _logger;

    public RVideoPromptGenerationAction(
        IServicePromptAssistantService assistant,
        ITodoXVideoPromptParser parser,
        ILogger<RVideoPromptGenerationAction> logger)
    {
        _assistant = assistant;
        _parser = parser;
        _logger = logger;
    }

    public async Task<RVideoPromptGenerationOutcome> GenerateAsync(RVideoPromptGenerationRequest request, CancellationToken ct = default)
    {
        if (request.ServiceId == Guid.Empty)
            throw new ArgumentException("ServiceId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.UserInput))
            throw new ArgumentException("UserInput is required.", nameof(request));
        if (request.CreativeMode && request.DurationSeconds is null or <= 0)
            throw new ArgumentException("Duration is required in creative mode.", nameof(request));

        var agentInput = PromptAssistantEndpoints.BuildAgentInput(new PromptAssistantGenerateRequest(
            ServiceId: request.ServiceId,
            UserInput: request.UserInput,
            Duration: request.CreativeMode ? request.DurationSeconds : null,
            VideoProjectId: request.VideoProjectId,
            CreativeMode: request.CreativeMode));

        var result = await _assistant.GeneratePromptAsync(
            request.ServiceId,
            agentInput,
            request.UserSession,
            ct,
            request.VideoProjectId,
            request.CharacterReference);

        if (!result.ValidationPassed || string.IsNullOrWhiteSpace(result.GeneratedJson))
        {
            _logger.LogWarning("RVIDEO_PROMPT_GENERATION_VALIDATION_FAILED serviceId={ServiceId} generationId={GenerationId} code={Code}", request.ServiceId, result.GenerationId, result.ErrorCode);
            return new RVideoPromptGenerationOutcome(result, _parser.Parse(result.GeneratedJson ?? string.Empty));
        }

        var parseResult = _parser.Parse(result.GeneratedJson);
        if (!parseResult.IsTodoXSchemaValid)
            _logger.LogWarning("RVIDEO_PROMPT_GENERATION_SCHEMA_INVALID serviceId={ServiceId} generationId={GenerationId}", request.ServiceId, result.GenerationId);

        return new RVideoPromptGenerationOutcome(result, parseResult);
    }
}

