using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;

namespace TodoX.Web.Components.Pages;

public partial class RenderVideoJobs
{
    private Guid? _characterDefaultAppliedServiceId;
    private bool _characterModeTouched;

    private void ApplyCharacterServiceDefaultIfNeeded(ServiceJobDefaults defaults, Guid serviceId)
    {
        if (_project is not null || !string.IsNullOrWhiteSpace(_prompt) || _characterModeTouched || _characterDefaultAppliedServiceId == serviceId)
        {
            return;
        }

        var mode = NormalizeCharacterServiceDefault(defaults.CharacterMode);
        _characterDefaultAppliedServiceId = serviceId;

        if (mode is null)
        {
            return;
        }

        ApplyCharacterServiceDefault(mode);
    }

    private void ApplyCharacterServiceDefault(string mode)
    {
        _selectedCharacter = null;
        _selectedCharacterId = null;
        _uploadedCharacter = null;

        if (string.Equals(mode, RVideoCharacterModes.None, StringComparison.OrdinalIgnoreCase))
        {
            _skipCharacter = true;
            _characterMode = RVideoCharacterModes.None;
            return;
        }

        _skipCharacter = false;
        _characterMode = mode;
    }

    private static string? NormalizeCharacterServiceDefault(string? value)
        => string.Equals(value, RVideoCharacterModes.None, StringComparison.OrdinalIgnoreCase)
            ? RVideoCharacterModes.None
            : string.Equals(value, RVideoCharacterModes.Upload, StringComparison.OrdinalIgnoreCase)
                ? RVideoCharacterModes.Upload
                : string.Equals(value, RVideoCharacterModes.Library, StringComparison.OrdinalIgnoreCase)
                    ? RVideoCharacterModes.Library
                    : null;
}
