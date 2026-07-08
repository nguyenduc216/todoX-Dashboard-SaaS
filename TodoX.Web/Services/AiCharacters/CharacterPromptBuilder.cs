using System.Text;

namespace TodoX.Web.Services.AiCharacters;

public sealed class CharacterPromptBuilder
{
    public string BuildNormalizedPrompt(string characterName, string description, string? stylePreset, string? gender, string aspectRatio)
    {
        stylePreset = CharacterPresetOptions.NormalizeOptionalPreset(stylePreset);
        gender = CharacterPresetOptions.NormalizeOptionalPreset(gender);

        var prompt = new StringBuilder();
        prompt.AppendLine("Create a consistent reusable AI character for TodoX customer videos.");
        prompt.AppendLine();
        prompt.AppendLine($"Character name: {characterName.Trim()}");
        prompt.AppendLine($"User description: {description.Trim()}");
        if (!string.IsNullOrWhiteSpace(gender))
        {
            prompt.AppendLine($"Gender: {gender}");
        }
        if (!string.IsNullOrWhiteSpace(stylePreset))
        {
            prompt.AppendLine($"Style preset: {stylePreset}");
        }

        prompt.AppendLine();
        prompt.AppendLine("Visual requirements:");
        prompt.AppendLine("- Premium, clean, high-quality character design");
        prompt.AppendLine("- Friendly and trustworthy personality");
        prompt.AppendLine("- Suitable for SaaS marketing videos, KOC videos, explainer videos and thumbnails");
        prompt.AppendLine("- Consistent face, hairstyle, outfit and body proportions");
        prompt.AppendLine("- Centered composition");
        prompt.AppendLine("- Sharp details");
        prompt.AppendLine("- Professional lighting");
        prompt.AppendLine("- Clean background suitable for later video composition");
        prompt.AppendLine("- No readable text");
        prompt.AppendLine("- No random logos");
        prompt.AppendLine("- No watermark");
        prompt.AppendLine("- No distorted hands");
        prompt.AppendLine("- No extra limbs");
        prompt.AppendLine("- No low-quality artifacts");

        if (!string.IsNullOrWhiteSpace(stylePreset))
        {
            prompt.AppendLine();
            prompt.AppendLine("Style rules:");
            prompt.AppendLine(BuildStyleRule(stylePreset));
        }

        prompt.AppendLine();
        prompt.AppendLine("Composition rules:");
        prompt.AppendLine(BuildAspectRatioRule(aspectRatio));
        prompt.AppendLine();
        prompt.AppendLine($"Avoid: {BuildNegativePrompt()}.");
        return prompt.ToString();
    }

    public string BuildNegativePrompt()
        => "unreadable text, random logo, watermark, distorted face, distorted hands, extra fingers, extra limbs, blurry image, low quality, duplicate character, cropped body";

    private static string BuildStyleRule(string stylePreset) => stylePreset switch
    {
        "3d_chibi" => "- Premium 3D chibi character\n- Cute but professional\n- Large expressive eyes\n- Modern business-friendly outfit",
        "realistic" => "- Realistic professional AI presenter\n- Natural face and body proportions\n- Studio quality portrait or full-body composition",
        "koc_ai" => "- Modern AI KOC presenter\n- Attractive, energetic, confident\n- Suitable for product introduction videos",
        "anime" => "- Polished anime-inspired character\n- Expressive face and clean linework\n- Professional commercial illustration quality",
        "cartoon" => "- Friendly cartoon character\n- Clear silhouette and expressive pose\n- Modern clean brand-safe design",
        "corporate_mascot" => "- Corporate mascot character\n- Trustworthy, memorable and brand-safe\n- Suitable for SaaS explainers and thumbnails",
        _ => "- Premium reusable AI character\n- Clean professional presentation\n- Brand-safe visual identity"
    };

    private static string BuildAspectRatioRule(string aspectRatio) => aspectRatio switch
    {
        "1:1" => "- Avatar style composition\n- Character clearly visible\n- Clean background",
        "9:16" => "- Full-body or half-body vertical composition\n- Leave space for video layout usage",
        "16:9" => "- Horizontal composition for thumbnails and video scenes\n- Leave clean negative space for layout usage",
        "4:5" => "- Portrait social composition\n- Character visible from head to waist or full body",
        "3:4" => "- Portrait composition\n- Character centered with clean reusable background",
        _ => "- Character clearly visible\n- Clean reusable background"
    };
}
