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
    public const string SceneImageVersion = "scene_image_version";
    public const string SceneStaticImage = "scene_static_image";
    public const string ProjectSourceImage = "project_source_image";
    public const string LegacyUploadedCharacter = "legacy_uploaded_character";
    public const string Missing = "missing";

    public static bool RequiresAiGeneration(
        VideoProjectSceneDto scene,
        RVideoJobSettingsDto? settings,
        SceneImageVersionDto? selectedImageVersion,
        VideoProjectDto? project = null)
        => !Resolve(scene, settings, selectedImageVersion, project).HasUsableInput;

    public static RVideoEffectiveSceneImageSource Resolve(
        VideoProjectSceneDto scene,
        RVideoJobSettingsDto? settings,
        SceneImageVersionDto? selectedImageVersion,
        VideoProjectDto? project = null)
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

        if (selectedImageVersion is not null
            && selectedImageVersion.Id != Guid.Empty
            && !string.IsNullOrWhiteSpace(selectedImageVersion.PublicUrl))
        {
            return new RVideoEffectiveSceneImageSource(
                false,
                selectedImageVersion.Id,
                selectedImageVersion.PublicUrl,
                selectedImageVersion.StorageKey,
                SceneImageVersion);
        }

        if (!string.IsNullOrWhiteSpace(scene.StaticImageUrl))
        {
            return new RVideoEffectiveSceneImageSource(
                false,
                null,
                scene.StaticImageUrl,
                scene.StaticImagePath,
                SceneStaticImage);
        }

        if (!string.IsNullOrWhiteSpace(project?.SourceImageUrl))
        {
            return new RVideoEffectiveSceneImageSource(
                false,
                null,
                project.SourceImageUrl,
                null,
                ProjectSourceImage);
        }

        if (!string.IsNullOrWhiteSpace(project?.UploadedCharacterUrl))
        {
            return new RVideoEffectiveSceneImageSource(
                false,
                null,
                project.UploadedCharacterUrl,
                null,
                LegacyUploadedCharacter);
        }

        return new RVideoEffectiveSceneImageSource(
            false,
            null,
            null,
            null,
            Missing);
    }
}
