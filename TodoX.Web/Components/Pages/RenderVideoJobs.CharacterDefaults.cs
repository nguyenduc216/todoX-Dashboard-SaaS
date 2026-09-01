using TodoX.Web.Models.Catalog;

namespace TodoX.Web.Components.Pages;

public partial class RenderVideoJobs
{
    private const string CharacterModeNoneDefault = "none";
    private const string CharacterModeUploadDefault = "upload";
    private const string CharacterModeLibraryDefault = "library";

    private Guid? _characterDefaultAppliedServiceId;

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (_project is not null || !string.IsNullOrWhiteSpace(_prompt))
        {
            return Task.CompletedTask;
        }

        var service = ResolveCurrentService();
        if (service is null || _characterDefaultAppliedServiceId == service.Id)
        {
            return Task.CompletedTask;
        }

        var defaults = ServiceJobDefaultsCodec.FromJson(service.JobDefaults.ToString());
        var mode = NormalizeCharacterServiceDefault(defaults.CharacterMode);
        _characterDefaultAppliedServiceId = service.Id;

        if (mode is null)
        {
            return Task.CompletedTask;
        }

        ApplyCharacterServiceDefault(mode);
        return InvokeAsync(StateHasChanged);
    }

    private void ApplyCharacterServiceDefault(string mode)
    {
        _selectedCharacter = null;
        _selectedCharacterId = null;
        _uploadedCharacter = null;

        if (string.Equals(mode, CharacterModeNoneDefault, StringComparison.OrdinalIgnoreCase))
        {
            _skipCharacter = true;
            return;
        }

        _skipCharacter = false;
        _characterMode = mode;
    }

    private static string? NormalizeCharacterServiceDefault(string? value)
        => string.Equals(value, CharacterModeNoneDefault, StringComparison.OrdinalIgnoreCase)
            ? CharacterModeNoneDefault
            : string.Equals(value, CharacterModeUploadDefault, StringComparison.OrdinalIgnoreCase)
                ? CharacterModeUploadDefault
                : string.Equals(value, CharacterModeLibraryDefault, StringComparison.OrdinalIgnoreCase)
                    ? CharacterModeLibraryDefault
                    : null;
}
