using System.Text.Json.Serialization;
using TodoX.Web.Models;

namespace TodoX.Web.Services.ImageRender;

public sealed class MarketingImageRenderRequest
{
    public string ServiceName { get; set; } = string.Empty;
    public string? ServiceCategory { get; set; }
    public string? ShortDescription { get; set; }
    public string Brief { get; set; } = string.Empty;
    public string Tone { get; set; } = "yellow_black";
    public string AspectRatio { get; set; } = "9:16";
    public string? BrandRobotImageUrl { get; set; }
    public IReadOnlyList<string> ReferenceImageUrls { get; set; } = Array.Empty<string>();
    public bool PreserveFixedAssets { get; set; } = true;
    public CurrentUserSession? User { get; set; }
    public MarketingRenderPlan? ExistingPlan { get; set; }
    public UniversalServiceImageRenderPlan? ExistingUniversalPlan { get; set; }
    public UserImageFeedback? Feedback { get; set; }
    public bool RecreatePlan { get; set; } = true;
}

public sealed class MarketingImageRenderResult
{
    public bool Ok { get; set; }
    public Guid RenderJobId { get; set; }
    public string LogCode { get; set; } = string.Empty;
    public string Status { get; set; } = "processing";
    public string? ImageUrl { get; set; }
    public string? Error { get; set; }
    public string AnalyzerProvider { get; set; } = "rule_v1";
    public bool UsedAnalyzerFallback { get; set; }
    public string CompiledPrompt { get; set; } = string.Empty;
    public MarketingRenderPlan? RenderPlan { get; set; }
    public UniversalServiceImageRenderPlan? UniversalPlan { get; set; }
    public ServiceImageQcResult? QcResult { get; set; }
    public List<MarketingImageRenderLogEntry> Logs { get; set; } = new();
}

public sealed class MarketingImageRenderLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string StepCode { get; set; } = string.Empty;
    public string StepName { get; set; } = string.Empty;
    public string Level { get; set; } = "info";
    public string Message { get; set; } = string.Empty;
    public object? Input { get; set; }
    public object? Output { get; set; }
    public object? Metadata { get; set; }
    public long? DurationMs { get; set; }
    public string? Error { get; set; }
}

public sealed class MarketingBriefAnalysis
{
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceCategory { get; set; } = string.Empty;
    public string DetectedServiceType { get; set; } = "general_service";
    public double Confidence { get; set; } = 0.7;
    public string ClassificationReason { get; set; } = string.Empty;
    public List<string> ExcludedServiceTypes { get; set; } = new();
    public bool MentionsTikTok { get; set; }
    public bool MentionsFacebook { get; set; }
    public bool MentionsReup { get; set; }
}

public sealed class MarketingRenderPlan
{
    public string PlanVersion { get; set; } = "1.0";
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceCategory { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = "9:16";
    public string Theme { get; set; } = "yellow_black";
    public string Headline { get; set; } = string.Empty;
    public string Subheadline { get; set; } = string.Empty;
    public string Footer { get; set; } = string.Empty;
    public List<string> BenefitBullets { get; set; } = new();
    public List<string> VisualElements { get; set; } = new();
    public MarketingAssetPolicy AssetPolicy { get; set; } = new();
    public List<string> ForbiddenTerms { get; set; } = new();
    public List<string> RequiredConcepts { get; set; } = new();
    public string NegativePrompt { get; set; } = string.Empty;
}

public sealed class MarketingAssetPolicy
{
    public bool PreserveBrandRobot { get; set; }
    public bool BrandRobotSentToModel { get; set; }
    public string RobotRole { get; set; } = "brand_robot";
    public string Pipeline { get; set; } = ImageRenderRequestModel.PipelineModelGenerate;
}
