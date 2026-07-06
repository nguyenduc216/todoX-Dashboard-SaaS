using System.Text;
using TodoX.Web.Models;

namespace TodoX.Web.Services.Images;

public sealed class ServiceImagePromptCompiler
{
    public CompiledImagePrompt Compile(UniversalServiceImageRenderPlan plan)
    {
        var p = new StringBuilder();
        p.AppendLine($"Create a premium {plan.AspectRatio} SaaS service thumbnail background.");
        p.AppendLine($"Theme: {DescribeTheme(plan.Theme)}, modern TodoX AI automation.");
        p.AppendLine($"Main message: {plan.CreativeBrief.MainMessage}");
        p.AppendLine($"Viewer should understand in 3 seconds: {plan.CreativeBrief.ViewerShouldUnderstandIn3Seconds}");
        p.AppendLine($"Visual metaphor: {plan.CreativeBrief.VisualMetaphor}");
        p.AppendLine();
        p.AppendLine("Main visual story:");
        p.AppendLine(plan.MainVisual.VisualStory);
        p.AppendLine();
        p.AppendLine("Required objects:");
        var requiredObjects = plan.ServiceType?.Equals("ai_video_from_prompt_character", StringComparison.OrdinalIgnoreCase) == true
            ? plan.MainVisual.Objects
                .Concat(new[]
                {
                    "prompt input panel",
                    "character/avatar selection card",
                    "background/scene selection card",
                    "render job status",
                    "vertical video preview frame",
                    "automated posting/scheduling status"
                })
            : plan.MainVisual.Objects;
        foreach (var obj in requiredObjects.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            p.AppendLine($"- {obj}");
        }
        p.AppendLine();
        p.AppendLine("Composition:");
        p.AppendLine("- Keep top area clean for headline overlay; do not render large readable text.");
        p.AppendLine("- Keep main visual centered and clear.");
        p.AppendLine("- Leave reserved space for brand character if required.");
        p.AppendLine("- Avoid small unreadable text and random logos.");
        p.AppendLine("- Use abstract UI labels or simple shapes instead of real brand logos unless provided.");
        if (plan.ServiceType?.Equals("ai_video_from_prompt_character", StringComparison.OrdinalIgnoreCase) == true)
        {
            p.AppendLine("- Do not add unrelated platform names, platform logos, repost workflows, or cross-channel movement concepts.");
            p.AppendLine("- Make the flow read as: user prompt plus selected character and scene become a rendered vertical video job.");
        }
        p.AppendLine(plan.MainVisual.Composition);
        p.AppendLine();
        p.AppendLine("Style:");
        p.AppendLine("- black and gold premium SaaS interface");
        p.AppendLine("- clean, sharp, high contrast, modern");
        p.AppendLine("- visual hierarchy, not cluttered");

        if (plan.FixedAssets.UseMrTodoX && !plan.FixedAssets.SendFixedAssetsToModel)
        {
            p.AppendLine();
            p.AppendLine("Fixed asset policy:");
            p.AppendLine("- Do not generate a robot, mascot, character, or human.");
            p.AppendLine("- Leave the brand asset zone empty for code compositing.");
        }

        return new CompiledImagePrompt
        {
            Prompt = p.ToString().Trim(),
            NegativePrompt = string.Join(", ", plan.NegativePromptTerms.Distinct())
        };
    }

    private static string DescribeTheme(string theme)
        => theme.Equals("yellow_black", StringComparison.OrdinalIgnoreCase) ? "black and gold" : theme;
}
