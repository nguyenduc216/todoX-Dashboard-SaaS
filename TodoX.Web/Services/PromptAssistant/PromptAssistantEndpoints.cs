using System.Text.Json;
using TodoX.Web.Services;

namespace TodoX.Web.Services.PromptAssistant;

public static class PromptAssistantEndpoints
{
    public static void MapPromptAssistantEndpoints(this WebApplication app)
    {
        app.MapPost("/api/prompt-assistant/generate", HandleGenerateAsync)
            .DisableAntiforgery();
    }

    internal static async Task<IResult> HandleGenerateAsync(
        PromptAssistantGenerateRequest request,
        IServicePromptAssistantService assistant,
        AuthStateService auth,
        CancellationToken ct)
    {
        if (auth.CurrentUser?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        var validationError = Validate(request);
        if (validationError is not null)
        {
            return Results.BadRequest(new PromptAssistantGenerateResponse(false, ErrorCode: "invalid_request", ErrorMessage: validationError));
        }

        try
        {
            var result = await assistant.GeneratePromptAsync(
                request.ServiceId,
                BuildAgentInput(request),
                auth.CurrentUser,
                ct,
                request.VideoProjectId);

            return Results.Json(ToResponse(result));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Results.Json(
                new PromptAssistantGenerateResponse(false, ErrorCode: ResolveErrorCode(ex), ErrorMessage: "Prompt Assistant generate failed."),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    internal static string BuildAgentInput(PromptAssistantGenerateRequest request)
    {
        var duration = request.Duration is > 0 ? request.Duration.Value : 30;
        var sceneCount = request.SceneCount is > 0 ? request.SceneCount.Value : 7;
        return $"{request.UserInput.Trim()}\n\nVideo duration: {duration} seconds. Scene count: {sceneCount}. Return only final TodoX prompt JSON.";
    }

    internal static PromptAssistantGenerateResponse ToResponse(ServicePromptGenerationResult result)
    {
        if (!result.ValidationPassed || string.IsNullOrWhiteSpace(result.GeneratedJson))
        {
            return new PromptAssistantGenerateResponse(
                false,
                GenerationId: result.GenerationId,
                Status: "FAIL",
                ErrorCode: string.IsNullOrWhiteSpace(result.ErrorCode) ? "generation_failed" : result.ErrorCode,
                ErrorMessage: "Prompt Assistant generate failed.");
        }

        return new PromptAssistantGenerateResponse(
            true,
            GenerationId: result.GenerationId,
            Status: "PASS",
            PromptJson: JsonDocument.Parse(result.GeneratedJson).RootElement.Clone(),
            Metrics: new PromptAssistantGenerateMetrics(
                result.PromptTokens,
                result.CompletionTokens,
                result.TotalTokens,
                result.Credit,
                result.FirstEventMs,
                result.FirstContentMs,
                result.TotalDurationMs));
    }

    private static string? Validate(PromptAssistantGenerateRequest request)
    {
        if (request.ServiceId == Guid.Empty) return "serviceId is required.";
        if (string.IsNullOrWhiteSpace(request.UserInput)) return "userInput is required.";
        if (request.Duration is <= 0) return "duration must be greater than zero.";
        if (request.SceneCount is <= 0) return "sceneCount must be greater than zero.";
        return null;
    }

    private static string ResolveErrorCode(Exception ex)
        => ex switch
        {
            ServicePromptDomainException domain => domain.Code,
            ServicePromptProviderException provider => provider.Code,
            ArgumentException => "invalid_request",
            _ => "prompt_assistant_failed"
        };
}

public sealed record PromptAssistantGenerateRequest(
    Guid ServiceId,
    string UserInput,
    int? Duration = 30,
    int? SceneCount = 7,
    long? VideoProjectId = null);

public sealed record PromptAssistantGenerateResponse(
    bool Success,
    Guid? GenerationId = null,
    string? Status = null,
    JsonElement? PromptJson = null,
    PromptAssistantGenerateMetrics? Metrics = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record PromptAssistantGenerateMetrics(
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens,
    decimal? Credit,
    int? FirstEventMs,
    int? FirstContentMs,
    int? TotalDurationMs);
