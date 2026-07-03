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
}

public sealed class ImageRenderRequestModel
{
    public string Prompt { get; set; } = string.Empty;
    public List<ReferenceImage> ReferenceImages { get; set; } = new();
    public int Count { get; set; } = 1;
    public string AspectRatio { get; set; } = "1:1";
    public string MimeType { get; set; } = "image/png";
    public string SafetyLevel { get; set; } = "block_low_and_above";
    public Guid? UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string FileCategory { get; set; } = "render";
    public bool RequireReferenceImages { get; set; } = false;
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
}

public interface IImageRenderService
{
    Task<ImageRenderResult> RenderAsync(ImageRenderRequestModel request, CancellationToken ct = default);
}
