using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace TodoX.Web.Services.ImageRender;

public interface IBrandAssetCompositeService
{
    Task<byte[]> ComposeServicePosterAsync(BrandAssetCompositeRequest request, CancellationToken ct = default);
}

public sealed class BrandAssetCompositeRequest
{
    public byte[] BackgroundBytes { get; set; } = Array.Empty<byte>();
    public ReferenceImage MainAsset { get; set; } = default!;
    public string AspectRatio { get; set; } = "9:16";
    public string Theme { get; set; } = "yellow_black";
    public string? Headline { get; set; }
    public string? Subheadline { get; set; }
    public string? Footer { get; set; }
    public BrandAssetCompositePlacement? Placement { get; set; }
    public int LoadedAssetByteLength { get; set; }
}

public sealed class BrandAssetCompositePlacement
{
    public int CanvasWidth { get; set; }
    public int CanvasHeight { get; set; }
    public int AssetX { get; set; }
    public int AssetY { get; set; }
    public int AssetWidth { get; set; }
    public int AssetHeight { get; set; }
}

public sealed class BrandAssetCompositeService : IBrandAssetCompositeService
{
    private readonly HttpClient _http;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<BrandAssetCompositeService> _logger;

    public BrandAssetCompositeService(HttpClient http, IWebHostEnvironment env, ILogger<BrandAssetCompositeService> logger)
    {
        _http = http;
        _env = env;
        _logger = logger;
    }

    public async Task<byte[]> ComposeServicePosterAsync(BrandAssetCompositeRequest request, CancellationToken ct = default)
    {
        if (request.BackgroundBytes.Length == 0)
        {
            throw new InvalidOperationException("Missing background image bytes.");
        }

        var assetBytes = await LoadReferenceBytesAsync(request.MainAsset, ct);
        if (assetBytes.Length == 0)
        {
            throw new InvalidOperationException("Missing fixed brand asset bytes.");
        }
        request.LoadedAssetByteLength = assetBytes.Length;

        using var backgroundStream = new MemoryStream(request.BackgroundBytes);
        using var background = await Image.LoadAsync<Rgba32>(backgroundStream, ct);
        NormalizeCanvas(background, request.AspectRatio);

        using var assetStream = new MemoryStream(assetBytes);
        using var asset = await Image.LoadAsync<Rgba32>(assetStream, ct);
        var canvasWidth = background.Width;
        var canvasHeight = background.Height;

        var targetHeight = (int)Math.Round(canvasHeight * 0.43);
        var scale = targetHeight / (double)asset.Height;
        var targetWidth = (int)Math.Round(asset.Width * scale);
        var maxWidth = (int)Math.Round(canvasWidth * 0.72);
        if (targetWidth > maxWidth)
        {
            scale = maxWidth / (double)asset.Width;
            targetWidth = maxWidth;
            targetHeight = (int)Math.Round(asset.Height * scale);
        }

        asset.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(targetWidth, targetHeight),
            Mode = ResizeMode.Max,
            Sampler = KnownResamplers.Lanczos3
        }));

        var x = (canvasWidth - asset.Width) / 2;
        var y = (int)Math.Round(canvasHeight * 0.52) - asset.Height / 2;
        y = Math.Clamp(y, (int)Math.Round(canvasHeight * 0.25), canvasHeight - asset.Height - 90);

        DrawBrandGlow(background, x, y, asset.Width, asset.Height);
        background.Mutate(ctx => ctx.DrawImage(asset, new Point(x, y), 1f));
        DrawPosterText(background, request);

        request.Placement = new BrandAssetCompositePlacement
        {
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            AssetX = x,
            AssetY = y,
            AssetWidth = asset.Width,
            AssetHeight = asset.Height
        };

        await using var output = new MemoryStream();
        await background.SaveAsPngAsync(output, new PngEncoder(), ct);
        return output.ToArray();
    }

    private async Task<byte[]> LoadReferenceBytesAsync(ReferenceImage image, CancellationToken ct)
    {
        if (image.Bytes?.Length > 0)
        {
            return image.Bytes;
        }

        if (!string.IsNullOrWhiteSpace(image.Base64))
        {
            return Convert.FromBase64String(image.Base64);
        }

        var url = image.Url ?? image.SourceUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return Array.Empty<byte>();
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return await _http.GetByteArrayAsync(absolute, ct);
        }

        var relativePath = url.Split('?', '#')[0].TrimStart('/', '\\')
            .Replace('/', System.IO.Path.DirectorySeparatorChar)
            .Replace('\\', System.IO.Path.DirectorySeparatorChar);
        var filePath = System.IO.Path.Combine(_env.WebRootPath, relativePath);
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Fixed brand asset file not found. url={Url} path={Path}", url, filePath);
            return Array.Empty<byte>();
        }

        return await File.ReadAllBytesAsync(filePath, ct);
    }

    private static void NormalizeCanvas(Image<Rgba32> image, string aspectRatio)
    {
        if (!aspectRatio.Equals("9:16", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var targetWidth = image.Width;
        var targetHeight = (int)Math.Round(targetWidth * 16 / 9d);
        if (targetHeight > image.Height)
        {
            targetHeight = image.Height;
            targetWidth = (int)Math.Round(targetHeight * 9 / 16d);
        }

        image.Mutate(x => x.Crop(new Rectangle(
            Math.Max(0, (image.Width - targetWidth) / 2),
            Math.Max(0, (image.Height - targetHeight) / 2),
            targetWidth,
            targetHeight)));
    }

    private static void DrawBrandGlow(Image<Rgba32> image, int x, int y, int width, int height)
    {
        var cx = x + width / 2f;
        var cy = y + height * 0.62f;
        var glowWidth = Math.Max(width * 1.2f, image.Width * 0.42f);
        var glowHeight = Math.Max(height * 0.72f, image.Height * 0.22f);
        var shadow = new EllipsePolygon(cx, cy, glowWidth / 2f, glowHeight / 2f);
        var gold = Color.FromRgba(255, 190, 20, 92);
        var dark = Color.FromRgba(0, 0, 0, 105);

        image.Mutate(ctx =>
        {
            ctx.Fill(dark, new EllipsePolygon(cx, cy + height * 0.18f, glowWidth / 2f, glowHeight / 3f));
            ctx.Fill(gold, shadow);
        });
    }

    private static void DrawPosterText(Image<Rgba32> image, BrandAssetCompositeRequest request)
    {
        var headline = string.IsNullOrWhiteSpace(request.Headline) ? "REUP VIDEO TIKTOK" : request.Headline.Trim();
        var subheadline = string.IsNullOrWhiteSpace(request.Subheadline) ? "SANG FACEBOOK" : request.Subheadline.Trim();
        var footer = string.IsNullOrWhiteSpace(request.Footer) ? "XÂY DỰNG KÊNH TỰ ĐỘNG" : request.Footer.Trim();

        var fontFamily = ResolveFontFamily();
        var headlineFont = fontFamily.CreateFont(Math.Max(36, image.Width / 12), FontStyle.Bold);
        var subFont = fontFamily.CreateFont(Math.Max(30, image.Width / 15), FontStyle.Bold);
        var footerFont = fontFamily.CreateFont(Math.Max(18, image.Width / 32), FontStyle.Bold);

        var gold = Color.FromRgb(255, 190, 20);
        var white = Color.FromRgb(245, 248, 255);
        var black = Color.FromRgba(0, 0, 0, 165);

        image.Mutate(ctx =>
        {
            DrawCenteredText(ctx, image.Width, headline, headlineFont, gold, black, image.Height * 0.055f);
            DrawCenteredText(ctx, image.Width, subheadline, subFont, white, black, image.Height * 0.13f);
            DrawCenteredText(ctx, image.Width, footer, footerFont, gold, black, image.Height * 0.915f);
        });
    }

    private static void DrawCenteredText(IImageProcessingContext ctx, int canvasWidth, string text, Font font, Color fill, Color shadow, float y)
    {
        var options = new RichTextOptions(font)
        {
            Origin = new PointF(canvasWidth / 2f, y),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };

        var shadowOptions = new RichTextOptions(font)
        {
            Origin = new PointF(canvasWidth / 2f + 2, y + 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };

        ctx.DrawText(shadowOptions, text, shadow);
        ctx.DrawText(options, text, fill);
    }

    private static FontFamily ResolveFontFamily()
    {
        foreach (var name in new[] { "Arial", "Segoe UI", "Tahoma", "DejaVu Sans", "Liberation Sans" })
        {
            if (SystemFonts.TryGet(name, out var family))
            {
                return family;
            }
        }

        return SystemFonts.Collection.Families.First();
    }
}
