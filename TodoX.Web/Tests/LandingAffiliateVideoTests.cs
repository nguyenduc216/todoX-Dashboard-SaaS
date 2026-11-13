using Xunit;

namespace TodoX.Web.Tests;

/// <summary>
/// The industry video section moved off the homepage to its own "Affiliate video" page,
/// and the homepage now previews the sample library instead.
/// </summary>
public sealed class LandingAffiliateVideoTests
{
    [Fact]
    public void AffiliateVideoPageCarriesTheIndustryVideoSection()
    {
        var html = Page("affiliate-video.html");

        Assert.Contains("id=\"industrySolutions\"", html);
        Assert.Contains("industrySwiper", html);
        Assert.Contains("id=\"industryModal\"", html);
        // landing.js uses these without a null check.
        Assert.Contains("id=\"header\"", html);
        Assert.Contains("id=\"navLinks\"", html);
        Assert.Contains("id=\"menuBtn\"", html);
        Assert.Contains("id=\"backTop\"", html);
        // ... and these libraries.
        foreach (var script in new[] { "/vendor/gsap/gsap.min.js", "/vendor/gsap/ScrollTrigger.min.js", "/vendor/swiper/swiper-bundle.min.js", "/vendor/aos/aos.js", "/js/landing.js" })
            Assert.Contains(script, html);
        // The homepage lead form is not here, so the modal's consult button must stay a plain link.
        Assert.DoesNotContain("data-industry-consult", html);
        Assert.Contains("href=\"/#contact\"", html);
    }

    [Fact]
    public void HomepageShowsVideoSamplesInsteadOfIndustryVideos()
    {
        var html = Page("index.html");

        Assert.DoesNotContain("industrySolutions", html);
        Assert.DoesNotContain("industryModal", html);
        Assert.Contains("id=\"homeSampleGrid\"", html);
        // On phones the cards are swiped, like the industry cards, instead of shrinking into a grid.
        Assert.Contains("swiper sampleSwiper", html);
        Assert.Contains("js/landing-samples-section.js", html);
        // The section title still opens the full library.
        Assert.Contains("<h2><a class=\"ls-title-link\" href=\"/mau-video\">", html);
        // Affiliate video is reachable from the menu, and no link points at the moved section.
        Assert.Contains("<a href=\"/affiliate-video\">Affiliate video</a>", html);
        Assert.DoesNotContain("#industries", html);
    }

    [Fact]
    public void AffiliateVideoPageIsRoutedAndListed()
    {
        var program = Page(".", "Program.cs");
        Assert.Contains("app.MapGet(\"/affiliate-video\"", program);
        Assert.DoesNotContain("/#industries", program);

        Assert.Contains("https://todox.vn/affiliate-video", Page("sitemap.xml"));
        Assert.Contains("BackPath = \"/affiliate-video\"", Page(".", "Services/VideoSharePage.cs"));
    }

    [Fact]
    public void HomepageSampleCardsAreBuiltWithoutInnerHtml()
    {
        var script = Page("js/landing-samples-section.js");

        Assert.DoesNotContain("innerHTML", script);
        Assert.Contains("/api/landing/video-samples", script);
        // Only samples with a real share page and real media become cards.
        Assert.Contains("validSharePath(video.sharePath)", script);
        Assert.Contains("validMedia(video.posterUrl, 'posters')", script);
        // The phone slider is built from the same cards.
        Assert.Contains("new Swiper('#homeSampleSwiper'", script);
        Assert.Contains("max-width: 768px", script);
    }

    private static string Page(string relative) => Page("wwwroot", relative);

    private static string Page(string folder, string relative) =>
        File.ReadAllText(Path.Combine(FindLandingRoot(), folder, relative));

    private static string FindLandingRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "TodoX.Landing");
            if (File.Exists(Path.Combine(candidate, "TodoX.Landing.csproj"))) return candidate;
        }
        throw new DirectoryNotFoundException("TodoX.Landing project not found.");
    }
}
