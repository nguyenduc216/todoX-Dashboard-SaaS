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
    public bool AssetHasAlphaBefore { get; set; }
    public bool AssetHasAlphaAfter { get; set; }
    public bool AssetBackgroundRemoved { get; set; }
    public string AssetBackgroundRemovalMethod { get; set; } = "none";
    public int AssetBackgroundRemovalTolerance { get; set; }
    public bool AssetCroppedTransparentPadding { get; set; }
    public int AssetOriginalWidth { get; set; }
    public int AssetOriginalHeight { get; set; }
    public int AssetProcessedWidth { get; set; }
    public int AssetProcessedHeight { get; set; }
    public double AssetOpaqueBrightPixelRatio { get; set; }
    public double AssetAspectRatio { get; set; }
    public double PlacementAspectRatio { get; set; }
    public double AspectRatioDelta { get; set; }
    public List<string> AssetWarnings { get; set; } = new();
    public List<ServicePosterTextBoxResult> TextOverlayResults { get; set; } = new();
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

public sealed class ServicePosterRect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class ServicePosterTextBoxResult
{
    public string Name { get; set; } = string.Empty;
    public bool Fit { get; set; }
    public ServicePosterRect Box { get; set; } = new();
    public ServicePosterRect UsedBounds { get; set; } = new();
    public int FontSize { get; set; }
    public int LineCount { get; set; }
    public string RenderedText { get; set; } = string.Empty;
    public string? Warning { get; set; }
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
        request.AssetOriginalWidth = asset.Width;
        request.AssetOriginalHeight = asset.Height;
        PrepareAssetTransparency(asset, request);
        CropTransparentPadding(asset, request);
        request.AssetProcessedWidth = asset.Width;
        request.AssetProcessedHeight = asset.Height;
        request.AssetOpaqueBrightPixelRatio = CalculateOpaqueBrightPixelRatio(asset);
        if (request.AssetOpaqueBrightPixelRatio > 0.18)
        {
            request.AssetWarnings.Add("Robot/logo có thể còn nền trắng. Hãy upload PNG nền trong suốt.");
        }
        var canvasWidth = background.Width;
        var canvasHeight = background.Height;

        var targetHeight = (int)Math.Round(canvasHeight * 0.35);
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
        request.AssetAspectRatio = request.AssetProcessedHeight == 0 ? 0 : request.AssetProcessedWidth / (double)request.AssetProcessedHeight;
        request.PlacementAspectRatio = asset.Height == 0 ? 0 : asset.Width / (double)asset.Height;
        request.AspectRatioDelta = request.AssetAspectRatio <= 0
            ? 0
            : Math.Abs(request.PlacementAspectRatio - request.AssetAspectRatio) / request.AssetAspectRatio;
        if (request.AspectRatioDelta > 0.10)
        {
            request.AssetWarnings.Add($"Robot/logo bị kéo dãn sai tỷ lệ: {request.AspectRatioDelta:P0}.");
        }

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

    private static void PrepareAssetTransparency(Image<Rgba32> asset, BrandAssetCompositeRequest request)
    {
        const int tolerance = 42;
        request.AssetBackgroundRemovalTolerance = tolerance;
        request.AssetHasAlphaBefore = HasMeaningfulAlpha(asset);

        if (request.AssetHasAlphaBefore)
        {
            request.AssetHasAlphaAfter = true;
            request.AssetBackgroundRemoved = false;
            request.AssetBackgroundRemovalMethod = "preserve_existing_alpha";
            return;
        }

        var background = AverageCornerColor(asset);
        if (!IsLightNeutral(background))
        {
            request.AssetHasAlphaAfter = false;
            request.AssetBackgroundRemoved = false;
            request.AssetBackgroundRemovalMethod = "none_non_light_corner_color";
            return;
        }

        var removed = 0;
        asset.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    ref var pixel = ref row[x];
                    var distance = ColorDistance(pixel, background);
                    if (distance <= tolerance)
                    {
                        pixel.A = 0;
                        removed++;
                    }
                    else if (distance <= tolerance + 18)
                    {
                        var alpha = (byte)Math.Clamp((distance - tolerance) / 18d * 255d, 80d, 255d);
                        pixel.A = (byte)Math.Min(pixel.A, alpha);
                    }
                }
            }
        });

        request.AssetHasAlphaAfter = HasMeaningfulAlpha(asset);
        request.AssetBackgroundRemoved = removed > 0;
        request.AssetBackgroundRemovalMethod = removed > 0 ? "corner_color_alpha_mask" : "none_no_matching_pixels";
    }

    private static void CropTransparentPadding(Image<Rgba32> image, BrandAssetCompositeRequest request)
    {
        var bounds = FindNonTransparentBounds(image);
        if (bounds == Rectangle.Empty)
        {
            return;
        }

        if (bounds.Width == image.Width && bounds.Height == image.Height)
        {
            return;
        }

        image.Mutate(x => x.Crop(bounds));
        request.AssetCroppedTransparentPadding = true;
    }

    private static Rectangle FindNonTransparentBounds(Image<Rgba32> image, byte alphaThreshold = 12)
    {
        var minX = image.Width;
        var minY = image.Height;
        var maxX = -1;
        var maxY = -1;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A > alphaThreshold)
                    {
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }
        });

        return maxX < minX || maxY < minY
            ? Rectangle.Empty
            : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static double CalculateOpaqueBrightPixelRatio(Image<Rgba32> image)
    {
        long totalOpaque = 0;
        long brightOpaque = 0;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    if (p.A <= 200)
                    {
                        continue;
                    }

                    totalOpaque++;
                    if (p.R > 235 && p.G > 235 && p.B > 235)
                    {
                        brightOpaque++;
                    }
                }
            }
        });

        return totalOpaque == 0 ? 0 : brightOpaque / (double)totalOpaque;
    }

    private static bool HasMeaningfulAlpha(Image<Rgba32> image)
    {
        var hasAlpha = false;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height && !hasAlpha; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A < 250)
                    {
                        hasAlpha = true;
                        break;
                    }
                }
            }
        });
        return hasAlpha;
    }

    private static Rgba32 AverageCornerColor(Image<Rgba32> image)
    {
        var sample = Math.Clamp(Math.Min(image.Width, image.Height) / 18, 4, 16);
        long r = 0;
        long g = 0;
        long b = 0;
        long count = 0;

        image.ProcessPixelRows(accessor =>
        {
            AddCorner(accessor, 0, 0, sample, ref r, ref g, ref b, ref count);
            AddCorner(accessor, accessor.Width - sample, 0, sample, ref r, ref g, ref b, ref count);
            AddCorner(accessor, 0, accessor.Height - sample, sample, ref r, ref g, ref b, ref count);
            AddCorner(accessor, accessor.Width - sample, accessor.Height - sample, sample, ref r, ref g, ref b, ref count);
        });

        if (count == 0)
        {
            return new Rgba32(255, 255, 255);
        }

        return new Rgba32((byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    private static void AddCorner(PixelAccessor<Rgba32> accessor, int startX, int startY, int size,
        ref long r, ref long g, ref long b, ref long count)
    {
        var x0 = Math.Clamp(startX, 0, Math.Max(0, accessor.Width - 1));
        var y0 = Math.Clamp(startY, 0, Math.Max(0, accessor.Height - 1));
        var x1 = Math.Min(accessor.Width, x0 + size);
        var y1 = Math.Min(accessor.Height, y0 + size);

        for (var y = y0; y < y1; y++)
        {
            var row = accessor.GetRowSpan(y);
            for (var x = x0; x < x1; x++)
            {
                r += row[x].R;
                g += row[x].G;
                b += row[x].B;
                count++;
            }
        }
    }

    private static bool IsLightNeutral(Rgba32 color)
    {
        var max = Math.Max(color.R, Math.Max(color.G, color.B));
        var min = Math.Min(color.R, Math.Min(color.G, color.B));
        var brightness = (color.R + color.G + color.B) / 3d;
        return brightness >= 178 && max - min <= 38;
    }

    private static double ColorDistance(Rgba32 a, Rgba32 b)
    {
        var dr = a.R - b.R;
        var dg = a.G - b.G;
        var db = a.B - b.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
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
        var headline = string.IsNullOrWhiteSpace(request.Headline) ? "TODOX AI" : request.Headline.Trim();
        var subheadline = string.IsNullOrWhiteSpace(request.Subheadline) ? "DỊCH VỤ TỰ ĐỘNG HÓA" : request.Subheadline.Trim();
        var footer = string.IsNullOrWhiteSpace(request.Footer) ? "TodoX" : request.Footer.Trim();

        var fontFamily = ResolveFontFamily();
        var gold = Color.FromRgb(255, 190, 20);
        var white = Color.FromRgb(245, 248, 255);
        var black = Color.FromRgba(0, 0, 0, 165);

        var margin = Math.Max(26, image.Width / 18);
        var headlineBox = new Rectangle(margin, Math.Max(24, image.Height / 28), image.Width - margin * 2, (int)Math.Round(image.Height * 0.12));
        var subBox = new Rectangle(margin, headlineBox.Bottom + Math.Max(8, image.Height / 150), image.Width - margin * 2, (int)Math.Round(image.Height * 0.08));
        var footerBox = new Rectangle(margin, (int)Math.Round(image.Height * 0.90), image.Width - margin * 2, (int)Math.Round(image.Height * 0.065));

        var results = new List<ServicePosterTextBoxResult>();
        image.Mutate(ctx =>
        {
            results.Add(DrawTextBox(ctx, "headline", headline, headlineBox, fontFamily, Math.Max(36, image.Width / 11), 24, gold, black, true));
            results.Add(DrawTextBox(ctx, "subheadline", subheadline, subBox, fontFamily, Math.Max(24, image.Width / 20), 16, white, black, true));
            results.Add(DrawTextBox(ctx, "footer", footer, footerBox, fontFamily, Math.Max(18, image.Width / 32), 12, gold, black, true));
        });
        request.TextOverlayResults = results;
    }

    private static ServicePosterTextBoxResult DrawTextBox(
        IImageProcessingContext ctx,
        string name,
        string text,
        Rectangle box,
        FontFamily fontFamily,
        int maxFontSize,
        int minFontSize,
        Color fill,
        Color shadow,
        bool bold)
    {
        var rendered = text;
        var fit = false;
        var warning = (string?)null;
        var fontSize = maxFontSize;
        List<string> lines = new();

        for (var size = maxFontSize; size >= minFontSize; size -= 2)
        {
            fontSize = size;
            lines = WrapText(rendered, size, box.Width);
            var lineHeight = (int)Math.Ceiling(size * 1.18);
            if (lines.Count * lineHeight <= box.Height)
            {
                fit = true;
                break;
            }
        }

        if (!fit)
        {
            var maxLines = Math.Max(1, box.Height / Math.Max(1, (int)Math.Ceiling(fontSize * 1.18)));
            lines = WrapText(rendered, fontSize, box.Width).Take(maxLines).ToList();
            if (lines.Count > 0)
            {
                lines[^1] = TruncateToWidth(lines[^1], fontSize, box.Width);
            }
            rendered = string.Join(" ", lines);
            warning = "text_truncated_or_shrunk";
        }

        var font = fontFamily.CreateFont(fontSize, bold ? FontStyle.Bold : FontStyle.Regular);
        var lineStep = (int)Math.Ceiling(fontSize * 1.18);
        var usedHeight = Math.Min(box.Height, lines.Count * lineStep);
        var usedWidth = Math.Min(box.Width, lines.Count == 0 ? 0 : lines.Max(x => EstimateTextWidth(x, fontSize)));

        for (var i = 0; i < lines.Count; i++)
        {
            var y = box.Y + i * lineStep;
            var options = new RichTextOptions(font)
            {
                Origin = new PointF(box.X, y),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            var shadowOptions = new RichTextOptions(font)
            {
                Origin = new PointF(box.X + 2, y + 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            ctx.DrawText(shadowOptions, lines[i], shadow);
            ctx.DrawText(options, lines[i], fill);
        }

        return new ServicePosterTextBoxResult
        {
            Name = name,
            Fit = fit,
            Box = ToPosterRect(box),
            UsedBounds = new ServicePosterRect { X = box.X, Y = box.Y, Width = usedWidth, Height = usedHeight },
            FontSize = fontSize,
            LineCount = lines.Count,
            RenderedText = rendered,
            Warning = warning
        };
    }

    private static List<string> WrapText(string text, int fontSize, int maxWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in words)
        {
            var candidate = string.IsNullOrWhiteSpace(current) ? word : $"{current} {word}";
            if (EstimateTextWidth(candidate, fontSize) <= maxWidth)
            {
                current = candidate;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(current))
            {
                lines.Add(current);
            }
            current = word;
        }

        if (!string.IsNullOrWhiteSpace(current))
        {
            lines.Add(current);
        }

        return lines.Count == 0 ? new List<string> { string.Empty } : lines;
    }

    private static string TruncateToWidth(string text, int fontSize, int maxWidth)
    {
        const string ellipsis = "...";
        var value = text.Trim();
        while (value.Length > 0 && EstimateTextWidth(value + ellipsis, fontSize) > maxWidth)
        {
            value = value[..^1].TrimEnd();
        }

        return string.IsNullOrWhiteSpace(value) ? ellipsis : value + ellipsis;
    }

    private static int EstimateTextWidth(string text, int fontSize)
        => (int)Math.Ceiling(text.Length * fontSize * 0.58);

    private static ServicePosterRect ToPosterRect(Rectangle rect)
        => new() { X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height };

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

