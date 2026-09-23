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

        var validationError = ValidateRequest(request);
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
        var userInput = request.UserInput;
        if (request.CreativeMode)
        {
            var targetDuration = request.Duration!.Value;
            return $"""
                CONTENT MODE: CREATIVE
                Treat the user's text as an idea or brief. You may create a hook, develop the content, add insight, conclusion, and a suitable call to action, and plan the scenes and narration.
                The user requests a target video duration of {targetDuration} seconds. Choose the scene count yourself; do not use a fixed scene count.
                Keep every scene duration_seconds an integer from 4 through 8 inclusive. Get the total as close to the target as possible while prioritizing coherent content, semantic scene boundaries, and valid scene durations. Never create a scene outside that range. Set the root duration to the sum of scene durations.

                For every scene return scene_purpose, duration_seconds, image_prompt, motion_prompt, voice, and tts_rate. Set tts_rate to a number from 1.0 through 1.2 inclusive; use 1.0 by default.
                Keep the existing TodoX JSON schema and return final JSON only, with no markdown fence or explanation.

                USER IDEA:
                {userInput}
                """;
        }

        return $"""
            CONTENT MODE: USER-PROVIDED SOURCE CONTENT
            Treat all user text below as the exact source content for the video's narration. Preserve every idea, its meaning, and its original order. Do not rewrite, paraphrase, omit, or add source narration. Do not invent a new hook, conclusion, call to action, or other content; keep the opening from the source.
            Analyze the source into content units, using punctuation (periods, commas, questions, exclamations), line breaks, and semantic boundaries as cues. Do not create one scene per punctuation mark. Merge units that are too short and split long units at semantic boundaries while preserving the source wording and order. Decide the scene count yourself; do not use a fixed scene count.
            Keep every scene duration_seconds an integer from 4 through 8 inclusive. Never create a scene outside that range. Set the root duration to the sum of scene durations.

            For every scene return scene_purpose, duration_seconds, image_prompt, motion_prompt, voice, and tts_rate. Distribute the source wording verbatim across voice fields. Set tts_rate to a number from 1.0 through 1.2 inclusive; use 1.0 by default.
            Keep the existing TodoX JSON schema and return final JSON only, with no markdown fence or explanation.

            SOURCE CONTENT:
            {userInput}
            """;
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

    internal static string? ValidateRequest(PromptAssistantGenerateRequest request)
    {
        if (request.ServiceId == Guid.Empty) return "serviceId is required.";
        if (string.IsNullOrWhiteSpace(request.UserInput)) return "userInput is required.";
        if (request.CreativeMode && request.Duration is null) return "duration is required in creative mode.";
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
    int? Duration = null,
    int? SceneCount = null,
    long? VideoProjectId = null,
    bool CreativeMode = false);

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
