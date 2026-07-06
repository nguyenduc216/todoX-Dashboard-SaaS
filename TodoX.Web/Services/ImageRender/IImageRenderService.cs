namespace TodoX.Web.Services.ImageRender;

public sealed class ReferenceImage
{
    public Guid? MediaId { get; set; }
    public string? Url { get; set; }
    public string? Base64 { get; set; }
    public string? Role { get; set; }
    public string? MimeType { get; set; }
    public byte[]? Bytes { get; set; }
    public string? FileName { get; set; }
    public string? DisplayName { get; set; }
    public string? PromptRoleDescription { get; set; }
    public long? SizeBytes { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public bool? HasAlpha { get; set; }
    public string? ObjectKey { get; set; }
    public string SourceType { get; set; } = "upload";
    public string? SourceUrl { get; set; }
}

public sealed class ImageRenderRequestModel
{
    public const string PipelineModelGenerate = "model_generate";
    public const string PipelineBackgroundThenComposite = "background_then_composite";

    public Guid? CorrelationId { get; set; }
    public string? LogCode { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public List<ReferenceImage> ReferenceImages { get; set; } = new();
    public string RenderPipeline { get; set; } = PipelineModelGenerate;
    public bool PreserveFixedAssets { get; set; }
    public string? Theme { get; set; }
    public string? PosterTextHeadline { get; set; }
    public string? PosterTextSubheadline { get; set; }
    public string? PosterTextFooter { get; set; }
    public int Count { get; set; } = 1;
    public string AspectRatio { get; set; } = "1:1";
    public string MimeType { get; set; } = "image/png";
    public string SafetyLevel { get; set; } = "block_low_and_above";
    public Guid? UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string FileCategory { get; set; } = "render";
    public bool RequireReferenceImages { get; set; } = false;
    public string? Gender { get; set; }
    public string? CharacterType { get; set; }
    public string? Outfit { get; set; }
    public string? CameraAngle { get; set; }
    public int? VariationIndex { get; set; }
    public int? ImageCount { get; set; }
}

public sealed class GeneratedImage
{
    public int Index { get; set; }
    public Guid MediaId { get; set; }
    public string? Url { get; set; }
}

public sealed class ImageRenderResult
{
    public bool Ok { get; set; }
    public List<GeneratedImage> Data { get; set; } = new();
    public int Count => Data.Count;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public Guid RequestId { get; set; }
    public bool UsedFallback { get; set; }
    public string? Error { get; set; }
    public List<RenderLogEntry> Logs { get; set; } = new();
}

public sealed class RenderLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Level { get; set; } = "info";
    public string Step { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
}

public interface IImageRenderService
{
    Task<ImageRenderResult> RenderAsync(ImageRenderRequestModel request, CancellationToken ct = default);
}
