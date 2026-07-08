namespace TodoX.Web.Services.AiCharacters;

public sealed class CharacterPromptBuilder
{
    public string BuildNormalizedPrompt(string characterName, string description, string stylePreset, string gender, string aspectRatio)
    {
        return $$"""
        Create a consistent reusable AI character for TodoX customer videos.

        Character name: {{characterName.Trim()}}
        User description: {{description.Trim()}}
        Gender: {{gender}}
        Style preset: {{stylePreset}}

        Visual requirements:
        - Premium, clean, high-quality character design
        - Friendly and trustworthy personality
        - Suitable for SaaS marketing videos, KOC videos, explainer videos and thumbnails
        - Consistent face, hairstyle, outfit and body proportions
        - Centered composition
        - Sharp details
        - Professional lighting
        - Clean background suitable for later video composition
        - No readable text
        - No random logos
        - No watermark
        - No distorted hands
        - No extra limbs
        - No low-quality artifacts

        Style rules:
        {{BuildStyleRule(stylePreset)}}

        Composition rules:
        {{BuildAspectRatioRule(aspectRatio)}}

        Avoid: {{BuildNegativePrompt()}}.
        """;
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
