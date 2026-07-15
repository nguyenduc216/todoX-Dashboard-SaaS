using TodoX.Web.Models;

namespace TodoX.Web.Services.Render;

/// <summary>
/// Builds the static-image prompt for a video scene, weaving in the selected character's identity so
/// every scene keeps a consistent face, hairstyle, outfit, body proportions and visual identity.
/// Shared by the interactive page and the background batch handler so both produce identical prompts.
/// </summary>
public static class SceneImagePromptBuilder
{
    public static string Build(string? imagePrompt, string? scenePrompt, string? characterNormalizedPrompt)
    {
        var basePrompt = string.IsNullOrWhiteSpace(imagePrompt) ? scenePrompt ?? string.Empty : imagePrompt!;
        if (string.IsNullOrWhiteSpace(characterNormalizedPrompt))
        {
            return basePrompt;
        }

        return $"Render a static image for this scene. Scene description: {basePrompt}. "
             + $"Keep the character identity consistent with this character description: {characterNormalizedPrompt}. "
             + "Maintain the same character across all scenes: face, hairstyle, outfit, body proportions, "
             + "and visual identity must stay consistent with the selected character and previously rendered scenes.";
    }

    public static string Build(VideoProjectSceneDto scene, string? characterNormalizedPrompt)
        => Build(scene.ImagePrompt, scene.ScenePrompt, characterNormalizedPrompt);
}
