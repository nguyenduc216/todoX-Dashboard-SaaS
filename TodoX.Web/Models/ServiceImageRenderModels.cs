namespace TodoX.Web.Models;

public sealed class UniversalServiceImageRenderPlan
{
    public string PlanVersion { get; set; } = "2.0";
    public string ServiceName { get; set; } = string.Empty;
    public string? ServiceCategory { get; set; }
    public string? ServiceType { get; set; }
    public string AspectRatio { get; set; } = "9:16";
    public string Theme { get; set; } = "yellow_black";
    public CreativeBrief CreativeBrief { get; set; } = new();
    public LayoutPlan Layout { get; set; } = new();
    public MainVisualPlan MainVisual { get; set; } = new();
    public FixedAssetPlan FixedAssets { get; set; } = new();
    public TextOverlayPlan TextOverlay { get; set; } = new();
    public QcPolicy QcPolicy { get; set; } = new();
    public List<string> NegativePromptTerms { get; set; } = new();
}

public sealed class CreativeBrief
{
    public string MainMessage { get; set; } = string.Empty;
    public string UserBenefit { get; set; } = string.Empty;
    public string VisualMetaphor { get; set; } = string.Empty;
    public string ViewerShouldUnderstandIn3Seconds { get; set; } = string.Empty;
    public List<string> KeyObjects { get; set; } = new();
    public List<string> RequiredConcepts { get; set; } = new();
    public string Mood { get; set; } = "modern, premium, clear, SaaS";
}

public sealed class LayoutPlan
{
    public string Template { get; set; } = "default_saas";
    public string AspectRatio { get; set; } = "9:16";
    public int CanvasWidth { get; set; } = 1080;
    public int CanvasHeight { get; set; } = 1920;
    public int SafeMargin { get; set; } = 72;
    public LayoutZone HeadlineZone { get; set; } = new()
    {
        Name = "headline",
        XPercent = 7,
        YPercent = 5,
        WidthPercent = 86,
        HeightPercent = 14,
        MaxLines = 2
    };
    public LayoutZone MainVisualZone { get; set; } = new()
    {
        Name = "main_visual",
        XPercent = 7,
        YPercent = 22,
        WidthPercent = 86,
        HeightPercent = 50
    };
    public LayoutZone BrandAssetZone { get; set; } = new()
    {
        Name = "brand_asset",
        XPercent = 58,
        YPercent = 63,
        WidthPercent = 34,
        HeightPercent = 27
    };
    public LayoutZone FooterZone { get; set; } = new()
    {
        Name = "footer",
        XPercent = 7,
        YPercent = 91,
        WidthPercent = 86,
        HeightPercent = 5,
        MaxLines = 1
    };
}

public sealed class LayoutZone
{
    public string Name { get; set; } = string.Empty;
    public double XPercent { get; set; }
    public double YPercent { get; set; }
    public double WidthPercent { get; set; }
    public double HeightPercent { get; set; }
    public int MaxLines { get; set; }
}

public sealed class MainVisualPlan
{
    public string VisualStory { get; set; } = string.Empty;
    public List<string> Objects { get; set; } = new();
    public string Composition { get; set; } = string.Empty;
    public string BackgroundPrompt { get; set; } = string.Empty;
}

public sealed class FixedAssetPlan
{
    public bool UseMrTodoX { get; set; } = true;
    public bool SendFixedAssetsToModel { get; set; }
    public string Pipeline { get; set; } = "background_then_composite";
    public bool RequireTransparentBackground { get; set; } = true;
    public bool PreserveAspectRatio { get; set; } = true;
    public double MaxHeightPercent { get; set; } = 27;
    public string PreferredPlacement { get; set; } = "bottom-right";
}

public sealed class TextOverlayPlan
{
    public string Headline { get; set; } = string.Empty;
    public string Subheadline { get; set; } = string.Empty;
    public List<string> MicroBullets { get; set; } = new();
    public string Footer { get; set; } = "TodoX AI";
    public int HeadlineMaxLines { get; set; } = 2;
    public int SubheadlineMaxLines { get; set; } = 2;
    public bool AvoidTooMuchText { get; set; } = true;
}

public sealed class QcPolicy
{
    public bool CheckDimensions { get; set; } = true;
    public bool CheckTextSafeArea { get; set; } = true;
    public bool CheckFixedAssetTransparency { get; set; } = true;
    public bool CheckFixedAssetAspectRatio { get; set; } = true;
    public bool CheckConceptCompleteness { get; set; } = true;
    public bool UseVisionQcIfAvailable { get; set; } = true;
}

public sealed class CompiledImagePrompt
{
    public string Prompt { get; set; } = string.Empty;
    public string NegativePrompt { get; set; } = string.Empty;
}

public sealed class ServiceImageQcResult
{
    public bool Passed { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public int Score { get; set; }
}

public sealed class UserImageFeedback
{
    public bool TextCropped { get; set; }
    public bool AssetDistortedOrWhiteBackground { get; set; }
    public bool ServiceMeaningUnclear { get; set; }
    public bool LayoutCluttered { get; set; }
    public bool TooMuchText { get; set; }
    public bool MissingWorkflowVisual { get; set; }
    public bool ColorNotGood { get; set; }
}
