namespace TodoX.Web.Components.Avatar;

public sealed class PublicAvatarPromptEditResult
{
    public string Prompt { get; set; } = string.Empty;
    public string CharacterTypeCode { get; set; } = "chibi";
    public string GenderCode { get; set; } = "neutral";
    public string CameraAngleCode { get; set; } = "half_body";
    public string OutfitCode { get; set; } = "suit";
    public bool RenderNow { get; set; }
}
