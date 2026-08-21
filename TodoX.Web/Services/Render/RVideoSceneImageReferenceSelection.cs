using System.Text.Json;
using TodoX.Web.Models;

namespace TodoX.Web.Services.Render;

public sealed record RVideoSceneImageReferenceSelection(
    bool ReferenceRequested,
    long? CharacterId,
    string? ObjectKey,
    string? Url,
    string? CharacterPrompt,
    string Source)
{
    public const string NoneSource = "NONE";
    public const string UploadSource = "UPLOAD";
    public const string LibrarySource = "LIBRARY";

    public static RVideoSceneImageReferenceSelection Resolve(
        bool skipCharacter,
        string? characterMode,
        string? uploadObjectKey,
        string? uploadUrl,
        long? libraryCharacterId,
        string? libraryObjectKey,
        string? libraryUrl,
        string? libraryCharacterPrompt)
    {
        if (skipCharacter || string.Equals(characterMode, RVideoCharacterModes.None, StringComparison.OrdinalIgnoreCase))
        {
            return new(false, null, null, null, null, NoneSource);
        }

        if (string.Equals(characterMode, RVideoCharacterModes.Upload, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(uploadObjectKey) && string.IsNullOrWhiteSpace(uploadUrl))
            {
                throw new InvalidOperationException("RVVIDEO_UPLOADED_CHARACTER_REFERENCE_UNAVAILABLE");
            }

            return new(true, null, uploadObjectKey, uploadUrl, null, UploadSource);
        }

        if (libraryCharacterId is null && string.IsNullOrWhiteSpace(libraryObjectKey) && string.IsNullOrWhiteSpace(libraryUrl))
        {
            throw new InvalidOperationException("RVVIDEO_LIBRARY_CHARACTER_REFERENCE_UNAVAILABLE");
        }

        return new(true, libraryCharacterId, libraryObjectKey, libraryUrl, libraryCharacterPrompt, LibrarySource);
    }

    public static RVideoSceneImageReferenceSelection Resolve(RVideoJobSettingsDto settings)
    {
        if (settings.SkipCharacter
            || string.Equals(settings.CharacterMode, RVideoCharacterModes.None, StringComparison.OrdinalIgnoreCase))
        {
            return new(false, null, null, null, null, NoneSource);
        }

        if (string.Equals(settings.CharacterMode, RVideoCharacterModes.Upload, StringComparison.OrdinalIgnoreCase))
        {
            var objectKey = ReadSnapshotString(settings.CharacterSnapshotJson, "storageKey", "objectKey");
            var url = ReadSnapshotString(settings.CharacterSnapshotJson, "fileUrl", "masterImageUrl", "url");
            if (string.IsNullOrWhiteSpace(objectKey) && string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException("RVVIDEO_UPLOADED_CHARACTER_REFERENCE_UNAVAILABLE");
            }

            return new(true, null, objectKey, url, null, UploadSource);
        }

        var libraryObjectKey = ReadSnapshotString(settings.CharacterSnapshotJson, "storageKey", "masterImageObjectKey", "objectKey");
        var libraryUrl = ReadSnapshotString(settings.CharacterSnapshotJson, "masterImageUrl", "fileUrl", "url");
        var characterPrompt = ReadSnapshotString(settings.CharacterSnapshotJson, "normalizedPrompt", "characterPrompt", "prompt");
        if (settings.SelectedCharacterId is null
            && string.IsNullOrWhiteSpace(libraryObjectKey)
            && string.IsNullOrWhiteSpace(libraryUrl))
        {
            throw new InvalidOperationException("RVVIDEO_LIBRARY_CHARACTER_REFERENCE_UNAVAILABLE");
        }

        return new(true, settings.SelectedCharacterId, libraryObjectKey, libraryUrl, characterPrompt, LibrarySource);
    }

    private static string? ReadSnapshotString(string? json, params string[] names)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var name in names)
            {
                if (document.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
