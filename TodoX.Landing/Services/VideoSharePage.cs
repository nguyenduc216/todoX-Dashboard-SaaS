using System.Text;
using System.Text.Json;
using TodoX.Landing.Models;

namespace TodoX.Landing.Services;

/// <summary>
/// Server-rendered page for /video/{slug}. Chat apps and Facebook do not run JavaScript,
/// so title, description, thumbnail and video must be in the Open Graph tags of the HTML itself.
/// </summary>
public static class VideoSharePage
{
    private const string FallbackImage = "/img/logo/todox-logo.png";
    private const int DescriptionLimit = 200;

    public static string Render(IndustrySolutionVideo video, IndustrySolution industry, string baseUrl)
    {
        var title = string.IsNullOrWhiteSpace(video.Title) ? industry.Title : video.Title;
        var pageTitle = $"{title} | Video mẫu TodoX";
        var description = Truncate(FirstNonEmpty(
            video.ShortDescription, video.Description, industry.ShortDescription, industry.Description,
            $"Video mẫu {industry.Title} do TodoX AI Video thực hiện."));
        var longDescription = FirstNonEmpty(video.Description, video.ShortDescription, industry.Description, industry.ShortDescription);
        var canonical = Absolute(baseUrl, video.SharePath);
        var image = Absolute(baseUrl, FirstNonEmpty(video.ThumbnailUrl, industry.ThumbnailUrl, FallbackImage));
        var videoUrl = Absolute(baseUrl, video.VideoUrl!);
        var landscape = video.AspectRatio == "16:9";
        var (width, height) = landscape ? (1280, 720) : (720, 1280);
        var related = industry.Videos
            .Where(v => v.Id != video.Id && !string.IsNullOrWhiteSpace(v.VideoUrl))
            .ToList();

        var jsonLd = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "VideoObject",
            ["name"] = title,
            ["description"] = description,
            ["thumbnailUrl"] = image,
            ["contentUrl"] = videoUrl,
            ["uploadDate"] = (video.CreatedAt ?? industry.CreatedAt)?.ToString("yyyy-MM-dd"),
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
              <meta property="og:title" content="{{E(title)}}">
              <meta property="og:description" content="{{E(description)}}">
              <meta property="og:url" content="{{E(canonical)}}">
              <meta property="og:image" content="{{E(image)}}">
              <meta property="og:image:secure_url" content="{{E(image)}}">
              <meta property="og:image:alt" content="{{E(title)}}">
              <meta property="og:video" content="{{E(videoUrl)}}">
              <meta property="og:video:secure_url" content="{{E(videoUrl)}}">
              <meta property="og:video:type" content="{{E(VideoMimeType(videoUrl))}}">
              <meta property="og:video:width" content="{{width}}">
              <meta property="og:video:height" content="{{height}}">
              <meta name="twitter:card" content="summary_large_image">
              <meta name="twitter:title" content="{{E(title)}}">
              <meta name="twitter:description" content="{{E(description)}}">
              <meta name="twitter:image" content="{{E(image)}}">
              <link rel="icon" href="/img/favicon/favicon.ico" sizes="any">
              <link rel="icon" href="/img/favicon/favicon.svg" type="image/svg+xml">
              <link rel="apple-touch-icon" href="/img/favicon/favicon.png">
              <link rel="stylesheet" href="/vendor/fonts/be-vietnam-pro.css">
              <link rel="stylesheet" href="/vendor/fontawesome/all.min.css">
              <link rel="stylesheet" href="/css/video-share.css?v=20261103-1">
              <script type="application/ld+json">{{jsonLd}}</script>
            </head>
            <body>
              <header class="vs-header">
                <a class="vs-brand" href="/" aria-label="TodoX trang chủ"><strong>Todo<span>X</span></strong></a>
                <a class="vs-header__link" href="/#industries"><i class="fa-solid fa-film"></i> Xem thêm video mẫu</a>
              </header>
              <main class="vs-main">
                <section class="vs-player {{(landscape ? "is-landscape" : "is-portrait")}}">
                  <div class="vs-player__media">
                    <video src="{{E(video.VideoUrl!)}}" poster="{{E(image)}}" controls playsinline preload="metadata"></video>
                  </div>
                  <div class="vs-player__content">
                    <span class="vs-eyebrow">VIDEO MẪU · {{E(industry.Title.ToUpperInvariant())}}</span>
                    <h1>{{E(title)}}</h1>
            """);

        if (!string.IsNullOrWhiteSpace(longDescription))
            html.Append($"""      <p class="vs-description">{E(longDescription)}</p>""").Append('\n');

        var notes = new[]
        {
            ("Định dạng", video.FormatNote),
            ("Mục tiêu", video.GoalNote),
            ("Năng lực", video.CapabilityNote)
        }.Where(x => !string.IsNullOrWhiteSpace(x.Item2)).ToList();
        if (notes.Count > 0)
        {
            html.Append("""      <div class="vs-notes">""").Append('\n');
            foreach (var (label, value) in notes)
                html.Append($"""        <div class="vs-note"><strong>{E(label)}</strong>{E(value!)}</div>""").Append('\n');
            html.Append("      </div>\n");
        }

        html.Append($$"""
                    <div class="vs-actions">
                      <a class="vs-btn vs-btn--primary" href="/?utm_source=video-share&amp;utm_medium=landing&amp;utm_content={{Uri.EscapeDataString(video.Slug)}}#contact">Tư vấn giải pháp này <i class="fa-solid fa-arrow-right"></i></a>
                      <button type="button" class="vs-btn" data-share-url="{{E(canonical)}}" data-share-title="{{E(title)}}"><i class="fa-solid fa-share-nodes"></i> <span>Chia sẻ</span></button>
                      <a class="vs-btn vs-btn--icon" href="https://www.facebook.com/sharer/sharer.php?u={{Uri.EscapeDataString(canonical)}}" target="_blank" rel="noopener" aria-label="Chia sẻ lên Facebook"><i class="fa-brands fa-facebook-f"></i></a>
                    </div>
                  </div>
                </section>
            """);

        if (related.Count > 0)
        {
            html.Append($$"""
                <section class="vs-related">
                  <h2>Video cùng ngành {{E(industry.Title)}}</h2>
                  <div class="vs-related__grid">
            """);
            foreach (var item in related)
            {
                var itemTitle = string.IsNullOrWhiteSpace(item.Title) ? industry.Title : item.Title;
                var thumb = FirstNonEmpty(item.ThumbnailUrl, industry.ThumbnailUrl);
                html.Append($$"""
                      <a class="vs-related__item" href="{{E(item.SharePath)}}">
                        <span class="vs-related__media">{{(thumb.Length > 0 ? $"""<img src="{E(thumb)}" alt="{E(itemTitle)}" loading="lazy">""" : "<span>TODOX</span>")}}<i class="fa-solid fa-play"></i></span>
                        <span class="vs-related__title">{{E(itemTitle)}}</span>
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
              <footer class="vs-footer">© TodoX · <a href="/">todox.vn</a></footer>
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
