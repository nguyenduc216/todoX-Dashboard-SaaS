namespace TodoX.Web.Components.Avatar;

public sealed class PublicAvatarPromptEditResult
{
    public string Prompt { get; set; } = string.Empty;
    public string CharacterTypeCode { get; set; } = "not_specified";
    public string GenderCode { get; set; } = "not_specified";
    public string CameraAngleCode { get; set; } = "not_specified";
    public string OutfitCode { get; set; } = "not_specified";
    public bool RenderNow { get; set; }
}
