using TodoX.Web.Models;
using TodoX.Web.Services.ImageRender;

namespace TodoX.Web.Services.Images;

public sealed class ServiceImageLayoutPlanner
{
    public UniversalServiceImageRenderPlan CreatePlan(
        MarketingImageRenderRequest request,
        MarketingBriefAnalysis analysis,
        MarketingRenderPlan aiPlan)
    {
        var creative = BuildCreativeBrief(request, analysis, aiPlan);
        var template = DetectLayoutTemplate(creative);
        var layout = BuildLayout(template, request.AspectRatio);

        return new UniversalServiceImageRenderPlan
        {
            ServiceName = analysis.ServiceName,
            ServiceCategory = analysis.ServiceCategory,
            ServiceType = analysis.DetectedServiceType,
            AspectRatio = string.IsNullOrWhiteSpace(request.AspectRatio) ? "9:16" : request.AspectRatio,
            Theme = string.IsNullOrWhiteSpace(request.Tone) ? "yellow_black" : request.Tone,
            CreativeBrief = creative,
            Layout = layout,
            MainVisual = BuildMainVisual(template, creative, aiPlan),
            FixedAssets = new FixedAssetPlan
            {
                UseMrTodoX = request.PreserveFixedAssets,
                SendFixedAssetsToModel = false,
                Pipeline = request.PreserveFixedAssets
                    ? ImageRenderRequestModel.PipelineBackgroundThenComposite
                    : ImageRenderRequestModel.PipelineModelGenerate,
                RequireTransparentBackground = request.PreserveFixedAssets,
                PreserveAspectRatio = true,
                MaxHeightPercent = template == "content_generation" ? 24 : 27,
                PreferredPlacement = template == "social_publishing" ? "bottom-center" : "bottom-right"
            },
            TextOverlay = BuildTextOverlay(aiPlan, creative),
            QcPolicy = new QcPolicy(),
            NegativePromptTerms = new List<string>
            {
                "small unreadable text",
                "random logo",
                "clutter",
                "cropped text",
                "distorted mascot",
                "white rectangle behind mascot"
            }
        };
    }

    public UniversalServiceImageRenderPlan ApplyFeedback(UniversalServiceImageRenderPlan plan, UserImageFeedback feedback)
    {
        if (feedback.TextCropped || feedback.TooMuchText)
        {
            plan.TextOverlay.Headline = Shorten(plan.TextOverlay.Headline, 34);
            plan.TextOverlay.Subheadline = Shorten(plan.TextOverlay.Subheadline, 42);
            plan.TextOverlay.MicroBullets.Clear();
            plan.Layout.HeadlineZone.HeightPercent = Math.Min(20, plan.Layout.HeadlineZone.HeightPercent + 4);
            plan.Layout.MainVisualZone.YPercent += 3;
            plan.Layout.MainVisualZone.HeightPercent = Math.Max(42, plan.Layout.MainVisualZone.HeightPercent - 4);
        }

        if (feedback.AssetDistortedOrWhiteBackground)
        {
            plan.FixedAssets.RequireTransparentBackground = true;
            plan.FixedAssets.MaxHeightPercent = Math.Max(20, plan.FixedAssets.MaxHeightPercent - 4);
            plan.NegativePromptTerms.Add("white box around brand character");
        }

        if (feedback.ServiceMeaningUnclear || feedback.MissingWorkflowVisual)
        {
            plan.CreativeBrief.VisualMetaphor = "A clear input to AI processing to finished output workflow.";
            plan.CreativeBrief.RequiredConcepts.Add("input to process to output workflow");
            plan.MainVisual.VisualStory = "A clear SaaS workflow scene: input panel on the left, TodoX AI automation path in the center, finished result preview on the right.";
            plan.MainVisual.Objects = new List<string> { "input panel", "AI processing path", "output preview", "dashboard status cards" };
        }

        if (feedback.LayoutCluttered)
        {
            plan.MainVisual.Composition += " Use fewer objects, more spacing, and one clear focal point.";
            plan.TextOverlay.MicroBullets.Clear();
        }

        if (feedback.ColorNotGood)
        {
            plan.Theme = "yellow_black";
            plan.CreativeBrief.Mood = "premium black and gold, clean contrast, TodoX brand";
        }

        return plan;
    }

    private static CreativeBrief BuildCreativeBrief(MarketingImageRenderRequest request, MarketingBriefAnalysis analysis, MarketingRenderPlan aiPlan)
    {
        var desc = $"{request.ShortDescription} {request.Brief}".Trim();
        return new CreativeBrief
        {
            MainMessage = string.IsNullOrWhiteSpace(aiPlan.Headline)
                ? $"Dịch vụ {analysis.ServiceName} giúp tự động hoá công việc bằng TodoX."
                : aiPlan.Headline,
            UserBenefit = string.IsNullOrWhiteSpace(desc)
                ? "Giúp người dùng thao tác nhanh hơn, trực quan hơn."
                : desc,
            VisualMetaphor = aiPlan.VisualElements.Count > 0
                ? string.Join(", ", aiPlan.VisualElements)
                : "A clean SaaS workflow showing input on the left, AI automation in the center, and finished output on the right.",
            ViewerShouldUnderstandIn3Seconds = $"TodoX hỗ trợ {analysis.ServiceName} bằng AI automation.",
            KeyObjects = aiPlan.VisualElements.Count > 0
                ? aiPlan.VisualElements
                : new List<string> { "input panel", "AI automation flow", "output preview", "dashboard interface" },
            RequiredConcepts = aiPlan.RequiredConcepts.Count > 0
                ? aiPlan.RequiredConcepts
                : new List<string> { "TodoX automation", "input to output", "AI assistant", "clear workflow" },
            Mood = "premium, clean, modern SaaS, black and gold"
        };
    }

    private static LayoutPlan BuildLayout(string template, string aspectRatio)
    {
        var layout = new LayoutPlan { Template = template, AspectRatio = aspectRatio };
        if (aspectRatio == "1:1")
        {
            layout.CanvasWidth = 1080;
            layout.CanvasHeight = 1080;
            layout.HeadlineZone.YPercent = 6;
            layout.HeadlineZone.HeightPercent = 18;
            layout.MainVisualZone.YPercent = 28;
            layout.MainVisualZone.HeightPercent = 46;
            layout.BrandAssetZone.XPercent = 62;
            layout.BrandAssetZone.YPercent = 62;
            layout.BrandAssetZone.HeightPercent = 28;
            layout.FooterZone.YPercent = 90;
        }
        else if (aspectRatio == "16:9")
        {
            layout.CanvasWidth = 1920;
            layout.CanvasHeight = 1080;
            layout.HeadlineZone.XPercent = 5;
            layout.HeadlineZone.YPercent = 8;
            layout.HeadlineZone.WidthPercent = 48;
            layout.MainVisualZone.XPercent = 42;
            layout.MainVisualZone.YPercent = 16;
            layout.MainVisualZone.WidthPercent = 52;
            layout.MainVisualZone.HeightPercent = 66;
            layout.BrandAssetZone.XPercent = 6;
            layout.BrandAssetZone.YPercent = 58;
            layout.BrandAssetZone.WidthPercent = 24;
            layout.FooterZone.YPercent = 88;
        }

        if (template == "content_generation")
        {
            layout.BrandAssetZone.XPercent = 62;
            layout.BrandAssetZone.WidthPercent = 28;
            layout.MainVisualZone.HeightPercent = Math.Max(44, layout.MainVisualZone.HeightPercent);
        }
        else if (template == "social_publishing")
        {
            layout.BrandAssetZone.XPercent = 33;
            layout.BrandAssetZone.WidthPercent = 34;
            layout.BrandAssetZone.YPercent = 65;
        }

        return layout;
    }

    private static MainVisualPlan BuildMainVisual(string template, CreativeBrief brief, MarketingRenderPlan aiPlan)
    {
        var story = template switch
        {
            "content_generation" => "A prompt input panel transforms through a glowing TodoX AI path into a polished generated output preview.",
            "social_publishing" => "A social publishing dashboard shows source content, scheduling automation, and destination channel status with clean data flow.",
            "data_report" => "A dashboard report scene with charts, KPI cards, trend arrows, and a clear analytics summary.",
            "customer_management" => "A CRM-style workspace with customer cards, automation status, and task flow.",
            _ => "A vertical SaaS workflow scene: input panel, AI automation flow, and finished output preview in a premium dashboard."
        };

        return new MainVisualPlan
        {
            VisualStory = story,
            Objects = brief.KeyObjects.Count > 0 ? brief.KeyObjects : new List<string> { "input panel", "AI automation flow", "output preview" },
            Composition = "Keep top area clean for headline overlay, center the main visual, reserve brand asset zone, avoid clutter.",
            BackgroundPrompt = aiPlan.NegativePrompt
        };
    }

    private static TextOverlayPlan BuildTextOverlay(MarketingRenderPlan aiPlan, CreativeBrief brief)
    {
        return new TextOverlayPlan
        {
            Headline = Shorten(string.IsNullOrWhiteSpace(aiPlan.Headline) ? brief.MainMessage : aiPlan.Headline, 44),
            Subheadline = Shorten(string.IsNullOrWhiteSpace(aiPlan.Subheadline) ? brief.UserBenefit : aiPlan.Subheadline, 58),
            Footer = Shorten(string.IsNullOrWhiteSpace(aiPlan.Footer) ? "TodoX AI" : aiPlan.Footer, 32),
            MicroBullets = aiPlan.BenefitBullets.Take(2).Select(x => Shorten(x, 28)).ToList(),
            HeadlineMaxLines = 2,
            SubheadlineMaxLines = 2,
            AvoidTooMuchText = true
        };
    }

    private static string DetectLayoutTemplate(CreativeBrief brief)
    {
        var text = $"{brief.MainMessage} {brief.UserBenefit} {brief.VisualMetaphor}".ToLowerInvariant();
        if (text.Contains("video") || text.Contains("prompt") || text.Contains("render") || text.Contains("ảnh")) return "content_generation";
        if (text.Contains("page") || text.Contains("facebook") || text.Contains("tiktok") || text.Contains("publish")) return "social_publishing";
        if (text.Contains("dashboard") || text.Contains("report") || text.Contains("analytics") || text.Contains("báo cáo")) return "data_report";
        if (text.Contains("customer") || text.Contains("khách hàng") || text.Contains("crm")) return "customer_management";
        return "default_saas";
    }

    private static string Shorten(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Trim();
        return text.Length <= max ? text : text[..Math.Max(0, max - 1)].Trim() + "…";
    }
}
