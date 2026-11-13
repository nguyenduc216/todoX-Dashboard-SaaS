using System.Text;
using System.Text.Json;
using TodoX.Landing.Data;
using TodoX.Landing.Models;

namespace TodoX.Landing.Services;

/// <summary>
/// Server-rendered page for a single shareable video (/video/{slug}, /mau-video/{slug}).
/// Chat apps and Facebook do not run JavaScript, so title, description, thumbnail and video
/// must be in the Open Graph tags of the HTML itself.
/// </summary>
public static class VideoSharePage
{
    private const string FallbackImage = "/img/logo/todox-logo.png";
    private const int DescriptionLimit = 200;

    public sealed record RelatedVideo(string Title, string SharePath, string? ThumbnailUrl);

    public sealed class Model
    {
        public required string Title { get; init; }
        public required string Slug { get; init; }
        public required string SharePath { get; init; }
        public required string VideoUrl { get; init; }
        public string? ThumbnailUrl { get; init; }
        // Image for Open Graph / Twitter previews; the player poster uses ThumbnailUrl.
        public string? ShareImageUrl { get; init; }
        public string? Description { get; init; }
        public string AspectRatio { get; init; } = "9:16";
        public DateTimeOffset? CreatedAt { get; init; }
        public required string Eyebrow { get; init; }
        public string FallbackDescription { get; init; } = "Video mẫu do TodoX AI Video thực hiện.";
        public IReadOnlyList<(string Label, string Value)> Notes { get; init; } = [];
        public required string BackPath { get; init; }
        public required string BackLabel { get; init; }
        public string? RelatedHeading { get; init; }
        public IReadOnlyList<RelatedVideo> Related { get; init; } = [];
    }

    public static Model FromIndustry(IndustrySolutionVideo video, IndustrySolution industry) => new()
    {
        Title = string.IsNullOrWhiteSpace(video.Title) ? industry.Title : video.Title,
        Slug = video.Slug,
        SharePath = video.SharePath,
        VideoUrl = video.VideoUrl!,
        ThumbnailUrl = FirstNonEmpty(video.ThumbnailUrl, industry.ThumbnailUrl),
        Description = FirstNonEmpty(video.Description, video.ShortDescription, industry.Description, industry.ShortDescription),
        AspectRatio = video.AspectRatio,
        CreatedAt = video.CreatedAt ?? industry.CreatedAt,
        Eyebrow = $"VIDEO MẪU · {industry.Title.ToUpperInvariant()}",
        FallbackDescription = $"Video mẫu {industry.Title} do TodoX AI Video thực hiện.",
        Notes = new[] { ("Định dạng", video.FormatNote), ("Mục tiêu", video.GoalNote), ("Năng lực", video.CapabilityNote) }
            .Where(x => !string.IsNullOrWhiteSpace(x.Item2)).Select(x => (x.Item1, x.Item2!.Trim())).ToList(),
        BackPath = "/affiliate-video",
        BackLabel = "Xem thêm video mẫu",
        RelatedHeading = $"Video cùng ngành {industry.Title}",
        Related = industry.Videos
            .Where(v => v.Id != video.Id && !string.IsNullOrWhiteSpace(v.VideoUrl))
            .Select(v => new RelatedVideo(string.IsNullOrWhiteSpace(v.Title) ? industry.Title : v.Title, v.SharePath,
                FirstNonEmpty(v.ThumbnailUrl, industry.ThumbnailUrl)))
            .ToList()
    };

    public static Model FromSample(VideoSample sample, VideoSampleCatalog catalog) => new()
    {
        Title = sample.Title,
        Slug = sample.Slug,
        SharePath = sample.SharePath,
        VideoUrl = sample.VideoUrl,
        // Link previews (Facebook/Zalo/Messenger) crop to ~1.91:1, so prefer the share image, then 16:9.
        ShareImageUrl = sample.ShareImageUrl ?? sample.ThumbnailLandscapeUrl ?? sample.PosterUrl,
        ThumbnailUrl = sample.PosterUrl,
        Description = sample.Description,
        AspectRatio = sample.AspectRatio,
        CreatedAt = sample.CreatedAt,
        Eyebrow = string.IsNullOrWhiteSpace(sample.GroupName)
            ? "KHO MẪU VIDEO"
            : $"MẪU VIDEO · {sample.GroupName.ToUpperInvariant()}",
        FallbackDescription = string.IsNullOrWhiteSpace(sample.GroupName)
            ? "Mẫu video do TodoX sản xuất."
            : $"Mẫu video {sample.GroupName} do TodoX sản xuất.",
        BackPath = string.IsNullOrWhiteSpace(sample.GroupCode)
            ? "/mau-video"
            : $"/mau-video?nhom={Uri.EscapeDataString(sample.GroupCode)}",
        BackLabel = "Xem kho mẫu video",
        RelatedHeading = string.IsNullOrWhiteSpace(sample.GroupName) ? "Mẫu video khác" : $"Mẫu video {sample.GroupName}",
        Related = catalog.Videos
            .Where(v => v.Id != sample.Id && v.GroupCode == sample.GroupCode)
            .Select(v => new RelatedVideo(v.Title, v.SharePath, v.PosterUrl))
            .ToList()
    };

    public static string Render(Model m, string baseUrl)
    {
        var pageTitle = $"{m.Title} | Video mẫu TodoX";
        var description = Truncate(FirstNonEmpty(m.Description, m.FallbackDescription));
        var canonical = Absolute(baseUrl, m.SharePath);
        var image = Absolute(baseUrl, FirstNonEmpty(m.ShareImageUrl, m.ThumbnailUrl, FallbackImage));
        var poster = Absolute(baseUrl, FirstNonEmpty(m.ThumbnailUrl, FallbackImage));
        var videoUrl = Absolute(baseUrl, m.VideoUrl);
        var landscape = m.AspectRatio == "16:9";
        var (width, height) = landscape ? (1280, 720) : (720, 1280);

        var jsonLd = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "VideoObject",
            ["name"] = m.Title,
            ["description"] = description,
            ["thumbnailUrl"] = image,
            ["contentUrl"] = videoUrl,
            ["uploadDate"] = m.CreatedAt?.ToString("yyyy-MM-dd"),
            ["publisher"] = new Dictionary<string, object?>
            {
                ["@type"] = "Organization",
                ["name"] = "TodoX",
                ["logo"] = Absolute(baseUrl, FallbackImage)
            }
        }).Replace("</", "<\\/");

        var html = new StringBuilder();
        html.Append($$"""
            <!DOCTYPE html>
            <html lang="vi">
            <head>
              <meta charset="UTF-8">
              <meta name="viewport" content="width=device-width, initial-scale=1.0">
              <title>{{E(pageTitle)}}</title>
              <meta name="description" content="{{E(description)}}">
              <link rel="canonical" href="{{E(canonical)}}">
              <meta name="theme-color" content="#ffbd22">
              <meta property="og:type" content="video.other">
              <meta property="og:locale" content="vi_VN">
              <meta property="og:site_name" content="TodoX">
              <meta property="og:title" content="{{E(m.Title)}}">
              <meta property="og:description" content="{{E(description)}}">
              <meta property="og:url" content="{{E(canonical)}}">
              <meta property="og:image" content="{{E(image)}}">
              <meta property="og:image:secure_url" content="{{E(image)}}">
              <meta property="og:image:alt" content="{{E(m.Title)}}">
              <meta property="og:video" content="{{E(videoUrl)}}">
              <meta property="og:video:secure_url" content="{{E(videoUrl)}}">
              <meta property="og:video:type" content="{{E(VideoMimeType(videoUrl))}}">
              <meta property="og:video:width" content="{{width}}">
              <meta property="og:video:height" content="{{height}}">
              <meta name="twitter:card" content="summary_large_image">
              <meta name="twitter:title" content="{{E(m.Title)}}">
              <meta name="twitter:description" content="{{E(description)}}">
              <meta name="twitter:image" content="{{E(image)}}">
              <link rel="icon" href="/img/favicon/favicon.ico" sizes="any">
              <link rel="icon" href="/img/favicon/favicon.svg" type="image/svg+xml">
              <link rel="apple-touch-icon" href="/img/favicon/favicon.png">
              <link rel="stylesheet" href="/vendor/fonts/be-vietnam-pro.css">
              <link rel="stylesheet" href="/vendor/fontawesome/all.min.css">
              <link rel="stylesheet" href="/css/video-share.css?v=20261103-2">
              <script type="application/ld+json">{{jsonLd}}</script>
            </head>
            <body>
              <header class="vp-header">
                <a class="vp-brand" href="/" aria-label="TodoX trang chủ"><strong>Todo<span>X</span></strong></a>
                <a class="vp-header__link" href="{{E(m.BackPath)}}"><i class="fa-solid fa-film"></i> {{E(m.BackLabel)}}</a>
              </header>
              <main class="vp-main">
                <section class="vp-player {{(landscape ? "is-landscape" : "is-portrait")}}">
                  <div class="vp-player__media">
                    <video src="{{E(m.VideoUrl)}}" poster="{{E(poster)}}" controls playsinline preload="auto"></video>
                  </div>
                  <div class="vp-player__content">
                    <span class="vp-eyebrow">{{E(m.Eyebrow)}}</span>
                    <h1>{{E(m.Title)}}</h1>

            """);

        if (!string.IsNullOrWhiteSpace(m.Description))
            html.Append($"""      <p class="vp-description">{E(m.Description.Trim())}</p>""").Append('\n');

        if (m.Notes.Count > 0)
        {
            html.Append("""      <div class="vp-notes">""").Append('\n');
            foreach (var (label, value) in m.Notes)
                html.Append($"""        <div class="vp-note"><strong>{E(label)}</strong>{E(value)}</div>""").Append('\n');
            html.Append("      </div>\n");
        }

        html.Append($$"""
                    <div class="vp-actions">
                      <a class="vp-btn vp-btn--primary" href="/?utm_source=video-share&amp;utm_medium=landing&amp;utm_content={{Uri.EscapeDataString(m.Slug)}}#contact">Tư vấn giải pháp này <i class="fa-solid fa-arrow-right"></i></a>
                      <a class="vp-btn" href="{{E(m.SharePath + LandingVideoDownload.RouteSuffix)}}" rel="nofollow" download><i class="fa-solid fa-download"></i> <span>Tải xuống</span></a>
                      <button type="button" class="vp-btn" data-share-url="{{E(canonical)}}" data-share-title="{{E(m.Title)}}"><i class="fa-solid fa-share-nodes"></i> <span>Chia sẻ</span></button>
                      <a class="vp-btn vp-btn--icon" href="https://www.facebook.com/sharer/sharer.php?u={{Uri.EscapeDataString(canonical)}}" target="_blank" rel="noopener" aria-label="Chia sẻ lên Facebook"><i class="fa-brands fa-facebook-f"></i></a>
                    </div>
                  </div>
                </section>

            """);

        if (m.Related.Count > 0)
        {
            html.Append($$"""
                <section class="vp-related">
                  <h2>{{E(m.RelatedHeading ?? "Video khác")}}</h2>
                  <div class="vp-related__grid">

            """);
            foreach (var item in m.Related)
            {
                var media = string.IsNullOrWhiteSpace(item.ThumbnailUrl)
                    ? "<span>TODOX</span>"
                    : $"""<img src="{E(item.ThumbnailUrl)}" alt="{E(item.Title)}" loading="lazy">""";
                html.Append($$"""
                      <a class="vp-related__item" href="{{E(item.SharePath)}}">
                        <span class="vp-related__media">{{media}}<i class="fa-solid fa-play"></i></span>
                        <span class="vp-related__title">{{E(item.Title)}}</span>
                      </a>

                """);
            }
            html.Append("""
                  </div>
                </section>

            """);
        }

        html.Append("""
              </main>
              <footer class="vp-footer">© TodoX · <a href="/">todox.vn</a></footer>
              <script src="/js/video-share.js?v=20261103-1" defer></script>
            </body>
            </html>
            """);

        return html.ToString();
    }

    public static string ResolveBaseUrl(HttpRequest request, IConfiguration configuration)
    {
        var configured = configuration["Landing:PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');
        return $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/');
    }

    private static string Absolute(string baseUrl, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme.StartsWith("http"))
            return absolute.ToString();
        return baseUrl + "/" + url.TrimStart('/');
    }

    private static string VideoMimeType(string url) =>
        url.Split('?')[0].EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ? "video/webm" : "video/mp4";

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static string Truncate(string value)
    {
        var singleLine = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length <= DescriptionLimit ? singleLine : singleLine[..(DescriptionLimit - 1)].TrimEnd() + "…";
    }

    // Escape only markup characters so Vietnamese text stays readable in the page source and previews.
    private static string E(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&#39;");
}
