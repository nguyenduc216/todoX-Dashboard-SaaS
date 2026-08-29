using TodoX.Web.Models;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public sealed record RVideoEffectiveSceneImageSource(
    bool UsesSharedReferenceImage,
    Guid? SelectedImageVersionId,
    string? SourceImageUrl,
    string? SourceImageObjectKey,
    string SourceLabel)
{
    public bool HasUsableInput
        => !string.IsNullOrWhiteSpace(SourceImageUrl)
           || !string.IsNullOrWhiteSpace(SourceImageObjectKey);
}

public static class RVideoEffectiveSceneImageSourceResolver
{
    public const string MissingSharedReferenceMessage = "Vui lòng cung cấp hình ảnh tham khảo trước khi tạo video.";

    public static RVideoEffectiveSceneImageSource Resolve(
        VideoProjectSceneDto scene,
        RVideoJobSettingsDto? settings,
        SceneImageVersionDto? selectedImageVersion)
    {
        if (settings?.UseReferenceImageForAllScenes == true)
        {
            var reference = RVideoSceneImageReferenceSelection.Resolve(settings);
            return new RVideoEffectiveSceneImageSource(
                true,
                null,
                reference.Url,
                reference.ObjectKey,
                "Ảnh tham khảo dùng chung");
        }

        return new RVideoEffectiveSceneImageSource(
            false,
            selectedImageVersion?.Id,
            selectedImageVersion?.PublicUrl,
            selectedImageVersion?.StorageKey,
            "Ảnh scene được AI tạo");
    }
}
