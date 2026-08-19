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
            return new(true, null, uploadObjectKey, uploadUrl, null, UploadSource);
        }

        return new(true, libraryCharacterId, libraryObjectKey, libraryUrl, libraryCharacterPrompt, LibrarySource);
    }
}
