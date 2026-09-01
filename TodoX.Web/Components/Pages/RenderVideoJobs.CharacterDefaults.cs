using TodoX.Web.Models.Catalog;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Components.Pages;

public partial class RenderVideoJobs
{
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
        if (string.Equals(mode, RVideoCharacterModes.None, StringComparison.OrdinalIgnoreCase))
        {
            _skipCharacter = true;
            _characterMode = RVideoCharacterModes.None;
            _selectedCharacter = null;
            _selectedCharacterId = null;
            _uploadedCharacter = null;
            return;
        }

        _skipCharacter = false;
        _uploadedCharacter = null;
        _selectedCharacter = null;
        _selectedCharacterId = null;
        _characterMode = string.Equals(mode, RVideoCharacterModes.Upload, StringComparison.OrdinalIgnoreCase)
            ? RVideoCharacterModes.Upload
            : RVideoCharacterModes.Library;
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
