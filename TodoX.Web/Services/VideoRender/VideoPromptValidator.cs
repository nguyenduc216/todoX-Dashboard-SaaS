using System.Text.Json;

namespace TodoX.Web.Services.VideoRender;

public sealed record VideoPromptValidationResult(
    bool IsValid,
    string ModelName,
    string TrimmedPrompt,
    int ActualCharacterCount,
    int? MaxCharacterCount,
    string? ErrorCode,
    string? Message);

public interface IVideoPromptValidator
{
    VideoPromptValidationResult Validate(
        string? prompt,
        string? modelName,
        string? capabilityConfigJson,
        int sceneIndex);
}

public sealed class VideoPromptValidator : IVideoPromptValidator
{
    public const int OmniFlashMaxPromptCharacters = 4096;
    public const int OmniFlashWarningPromptCharacters = 3800;
    public const string RequiredErrorCode = "video_prompt_required";
    public const string TooLongErrorCode = "video_prompt_too_long";

    public VideoPromptValidationResult Validate(
        string? prompt,
        string? modelName,
        string? capabilityConfigJson,
        int sceneIndex)
    {
        var normalizedModel = string.IsNullOrWhiteSpace(modelName) ? string.Empty : modelName.Trim();
        var trimmedPrompt = prompt?.Trim() ?? string.Empty;
        var actualCharacters = CountUnicodeScalars(trimmedPrompt);
        var maxCharacters = ResolveMaxPromptCharacters(normalizedModel, capabilityConfigJson);

        if (actualCharacters == 0)
        {
            return new VideoPromptValidationResult(
                false,
                normalizedModel,
                trimmedPrompt,
                actualCharacters,
                maxCharacters,
                RequiredErrorCode,
                $"Scene {sceneIndex:00}: prompt video không được để trống.");
        }

        if (maxCharacters is int max && actualCharacters > max)
        {
            return new VideoPromptValidationResult(
                false,
                normalizedModel,
                trimmedPrompt,
                actualCharacters,
                max,
                TooLongErrorCode,
                $"Scene {sceneIndex:00}: prompt video có {actualCharacters:n0} ký tự, vượt giới hạn {max:n0} ký tự của model {normalizedModel}.");
        }

        return new VideoPromptValidationResult(
            true,
            normalizedModel,
            trimmedPrompt,
            actualCharacters,
            maxCharacters,
            null,
            null);
    }

    public static int CountUnicodeScalars(string value)
        => value.EnumerateRunes().Count();

    public static int? ResolveMaxPromptCharacters(string? modelName, string? capabilityConfigJson)
    {
        var configured = ReadMaxPromptCharacters(capabilityConfigJson);
        if (configured is > 0)
        {
            return configured.Value;
        }

        return string.Equals(modelName?.Trim(), "omni-flash", StringComparison.OrdinalIgnoreCase)
            ? OmniFlashMaxPromptCharacters
            : null;
    }

    private static int? ReadMaxPromptCharacters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("max_prompt_characters", out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)
                ? number
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
