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
        foreach (var obj in plan.MainVisual.Objects.Distinct())
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
