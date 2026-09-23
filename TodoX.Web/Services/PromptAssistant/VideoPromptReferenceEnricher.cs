using System.Text.Json.Nodes;
using TodoX.Web.Models;

namespace TodoX.Web.Services.PromptAssistant;

public static class VideoPromptReferenceEnricher
{
    private const string ReferenceInstruction =
        "Use the provided reference image as the visual reference for the main character. "
        + "Maintain the character's identity and relevant appearance while generating the scene image.";

    public static string Enrich(string generatedJson, RVideoJobSettingsDto? settings)
    {
        if (settings is null
            || settings.UseReferenceImageForAllScenes
            || !HasUsableReference(settings))
        {
            return generatedJson;
        }

        var root = JsonNode.Parse(generatedJson);
        if (root is not JsonObject prompt
            || prompt["scenes"] is not JsonArray scenes)
        {
            return generatedJson;
        }

        foreach (var scene in scenes.OfType<JsonObject>())
        {
            if (scene["image_prompt"] is not JsonValue imagePromptNode
                || !imagePromptNode.TryGetValue<string>(out var imagePrompt)
                || string.IsNullOrWhiteSpace(imagePrompt)
                || ContainsReferenceInstruction(imagePrompt))
            {
                continue;
            }

            scene["image_prompt"] = $"{ReferenceInstruction}\n\nSCENE:\n{imagePrompt}";
        }

        return prompt.ToJsonString(ServicePromptJson.Options);
    }

    private static bool HasUsableReference(RVideoJobSettingsDto settings)
    {
        if (settings.SkipCharacter
            || string.Equals(settings.CharacterMode, RVideoCharacterModes.None, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var snapshot = JsonNode.Parse(
            string.IsNullOrWhiteSpace(settings.CharacterSnapshotJson) ? "{}" : settings.CharacterSnapshotJson) as JsonObject;
        if (string.Equals(settings.CharacterMode, RVideoCharacterModes.Upload, StringComparison.OrdinalIgnoreCase))
        {
            return HasSnapshotValue(snapshot, "fileUrl", "masterImageUrl", "url", "storageKey", "objectKey");
        }

        return settings.SelectedCharacterId is not null
            || HasSnapshotValue(snapshot, "masterImageUrl", "fileUrl", "url", "masterImageObjectKey", "storageKey", "objectKey");
    }

    private static bool HasSnapshotValue(JsonObject? snapshot, params string[] names)
        => snapshot is not null
           && names.Any(name => snapshot.TryGetPropertyValue(name, out var value)
                                && value is JsonValue jsonValue
                                && jsonValue.TryGetValue<string>(out var text)
                                && !string.IsNullOrWhiteSpace(text));

    private static bool ContainsReferenceInstruction(string prompt)
        => prompt.Contains("reference image", StringComparison.OrdinalIgnoreCase)
           || prompt.Contains("attached reference", StringComparison.OrdinalIgnoreCase)
           || prompt.Contains("MAIN CHARACTER IDENTITY", StringComparison.OrdinalIgnoreCase)
           || prompt.Contains("character identity", StringComparison.OrdinalIgnoreCase);
}
