using System.Text.Json;
using System.Text.Json.Nodes;
using TodoX.Web.Models;

namespace TodoX.Web.Services.PromptAssistant;

public sealed record PromptAssistantCharacterReference(
    string Mode,
    string Image,
    string? Name = null,
    string? Source = null)
{
    public bool IsEnabled => string.Equals(Mode, RVideoCharacterModes.Library, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Mode, RVideoCharacterModes.Upload, StringComparison.OrdinalIgnoreCase);

    public static PromptAssistantCharacterReference None { get; } = new(RVideoCharacterModes.None, string.Empty);
}

public static class PromptAssistantCharacterReferenceSynchronizer
{
    private const string Header = "CHARACTER REFERENCE - REQUIRED";
    private const string SceneMarker = "SCENE:";
    private const string LegacyInstruction =
        "Use the provided reference image as the visual reference for the main character. "
        + "Maintain the character's identity and relevant appearance while generating the scene image.";

    public static string Synchronize(string json, PromptAssistantCharacterReference? reference)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;

        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new JsonException("Prompt JSON must be an object.");
        var active = Normalize(reference);
        root["character_reference_mode"] = active is not null ? "image_reference_required" : "none";
        root["character_reference_image"] = active?.Image ?? string.Empty;
        root["character_reference"] = active is not null
            ? new JsonObject
            {
                ["source"] = active.Source ?? string.Empty,
                ["name"] = active.Name ?? string.Empty,
                ["image"] = active.Image
            }
            : null;

        if (root["scenes"] is not JsonArray scenes) return root.ToJsonString(ServicePromptJson.Options);

        foreach (var scene in scenes.OfType<JsonObject>())
        {
            if (scene["image_prompt"] is not JsonValue value
                || !value.TryGetValue<string>(out var original))
            {
                continue;
            }

            var scenePrompt = RemoveReferenceContract(original);
            var applies = HasExplicitTrue(scene, "main_character_present");
            scene["image_prompt"] = active is not null && applies
                ? BuildContract(active, scenePrompt)
                : scenePrompt;
        }

        return root.ToJsonString(ServicePromptJson.Options);
    }

    public static PromptAssistantCharacterReference FromSettings(RVideoJobSettingsDto? settings)
    {
        if (settings is null || settings.SkipCharacter || string.Equals(settings.CharacterMode, RVideoCharacterModes.None, StringComparison.OrdinalIgnoreCase))
            return PromptAssistantCharacterReference.None;

        var snapshot = ParseObject(settings.CharacterSnapshotJson);
        var image = settings.CharacterMode.Equals(RVideoCharacterModes.Upload, StringComparison.OrdinalIgnoreCase)
            ? Read(snapshot, "fileUrl", "url", "storageKey", "objectKey")
            : Read(snapshot, "masterImageUrl", "fileUrl", "url", "masterImageObjectKey", "storageKey", "objectKey");
        if (string.IsNullOrWhiteSpace(image)) return PromptAssistantCharacterReference.None;

        return new(
            settings.CharacterMode,
            image,
            Read(snapshot, "name", "characterName"),
            Read(snapshot, "source") ?? settings.CharacterMode);
    }

    private static PromptAssistantCharacterReference? Normalize(PromptAssistantCharacterReference? reference)
        => reference is { IsEnabled: true } && !string.IsNullOrWhiteSpace(reference.Image)
            ? reference with { Image = reference.Image.Trim() }
            : null;

    private static string BuildContract(PromptAssistantCharacterReference reference, string scene)
        => $"{Header}\n\nUse the provided character reference image as the authoritative visual reference for the main character.\n\n"
         + "The main character MUST preserve the identity and visual design from the provided reference image, including face, head shape, hair, body proportions, clothing, clothing colors, silhouette and other defining visual characteristics.\n\n"
         + "Do not redesign, reinterpret, replace or invent a different character.\n\n"
         + "The reference image has priority over any generic character description in this prompt.\n\n"
         + $"Selected character: {reference.Name ?? "selected reference"}\n\n{SceneMarker}\n{scene}";

    private static string RemoveReferenceContract(string prompt)
    {
        var text = prompt.Trim();
        if (text.StartsWith(Header, StringComparison.OrdinalIgnoreCase))
        {
            var scene = text.IndexOf(SceneMarker, Header.Length, StringComparison.OrdinalIgnoreCase);
            return scene >= 0 ? text[(scene + SceneMarker.Length)..].TrimStart() : string.Empty;
        }

        if (text.StartsWith("REFERENCE IMAGE:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Use the provided reference image", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith(LegacyInstruction, StringComparison.OrdinalIgnoreCase))
        {
            var scene = text.IndexOf(SceneMarker, StringComparison.OrdinalIgnoreCase);
            return scene >= 0 ? text[(scene + SceneMarker.Length)..].TrimStart() : string.Empty;
        }

        return text.Replace(LegacyInstruction, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
    }

    private static bool HasExplicitTrue(JsonObject scene, string property)
        => scene[property] is JsonValue value && value.TryGetValue<bool>(out var result) && result;

    private static JsonObject? ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return null; }
    }

    private static string? Read(JsonObject? source, params string[] names)
        => names.Select(name => source?[name]?.GetValue<string>())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
