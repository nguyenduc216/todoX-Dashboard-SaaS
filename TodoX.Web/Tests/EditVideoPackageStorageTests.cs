using System.Text.Json.Nodes;
using TodoX.Web.Services.EditVideoTool;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>Packages give points, license and storage; files for 79AI live in the customer's space; one 79AI project per project.</summary>
public sealed class EditVideoPackageStorageTests
{
    [Fact]
    public void AiProjectIsNamedCustomerCodeThenProjectName()
    {
        Assert.Equal("KH0012 - Nhà phố Quận 7", EditVideoAiProjectService.ProviderName("KH0012", " Nhà phố Quận 7 "));
        Assert.Equal("KH - Dự án A", EditVideoAiProjectService.ProviderName(null, "Dự án A"));
        Assert.True(EditVideoAiProjectService.ProviderName("KH1", new string('a', 300)).Length <= 120);
    }

    [Theory]
    [InlineData("""{"data":{"id_base":"p1"}}""", "p1")]
    [InlineData("""{"data":{"project":{"id_base":"p2"}}}""", "p2")]
    [InlineData("""{"id_base":"p3"}""", "p3")]
    [InlineData("""{"error":1,"message":"x"}""", null)]
    public void AiProjectIdIsReadWhereverGommoPutsIt(string json, string? expected) =>
        Assert.Equal(expected, EditVideoAiProjectService.ReadId(JsonNode.Parse(json)));

    [Fact]
    public void ProjectKeysAndFileNamesAreSafe()
    {
        Assert.Equal("nha-pho-quan-7", EditVideoStorageService.ProjectKey("Nhà phố Quận 7"));
        Assert.Equal("dong-thap", EditVideoStorageService.ProjectKey("  Đồng Tháp! "));
        Assert.Equal("anh-mat-tien.jpg", EditVideoStorageService.SafeFileName(@"C:\x\..\Ảnh mặt tiền.JPG")?.Replace(".JPG", ".jpg"));
        Assert.Null(EditVideoStorageService.SafeFileName("   "));
        Assert.Equal("file.mp4", EditVideoStorageService.SafeFileName("!!!.mp4"));
    }

    [Fact]
    public void PackageCycleRunsToTheEndOfItsLastDayInVietnam()
    {
        var end = PackageEntitlementService.EndOfDay(new DateTime(2026, 12, 31))!.Value;
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.FromHours(7)), end.AddTicks(-(end.Ticks % TimeSpan.TicksPerSecond)));
        Assert.Null(PackageEntitlementService.EndOfDay(null));
        Assert.Equal(1024L * 1024 * 1024, PackageEntitlement.DefaultStorageBytes);
    }

    [Fact]
    public void WrongAccountPasswordIsUnauthorized() =>
        Assert.Equal(401, EditVideoEndpoints.StatusFor(EditVideoErrorCodes.InvalidAccount));

    [Fact]
    public void AiTasksGoIntoTheProjectTheyWereCreatedIn()
    {
        var fields = EditVideoAiService.CreateFields("image", "m", new EditVideoAiCreateRequest { Prompt = "p" }, new EditVideoAiSelection(null, null, null, null), "proj-123");
        Assert.Equal("proj-123", fields["project_id"]);
        Assert.Equal("default", EditVideoAiService.CreateFields("image", "m", new EditVideoAiCreateRequest { Prompt = "p" }, new EditVideoAiSelection(null, null, null, null))["project_id"]);
    }

    private static readonly Dictionary<string, decimal> WebRates = new()
    {
        ["image:standard"] = 0.5m, ["image:premium"] = 1m, ["video:standard"] = 0.8m, ["video:premium"] = 1.5m
    };

    [Fact]
    public void AiPointsFollowTodoXRatesNotProviderCredits()
    {
        var prices = new EditVideoAiPrices(new EditVideoAiSettings { PremiumModels = "veo_omni" }, WebRates);
        Assert.Equal(0.5m, prices.For("image", "seedream_4_5", null));
        Assert.Equal(6.4m, prices.For("video", "kling_4_0", "8"));          // 8 s × 0.8
        Assert.Equal(12m, prices.For("video", "veo_omni", "8s"));            // premium model: 8 s × 1.5
        Assert.Equal(EditVideoAiPrices.DefaultVideoSeconds * 0.8m, prices.For("video", "x", null));
        Assert.Null(prices.For("music", "suno-v5.5", null));                 // no music price yet → refused, never guessed
        Assert.NotNull(prices.Missing("music", "suno-v5.5"));

        var withMusic = prices with { Settings = prices.Settings with { MusicPoints = 3m } };
        Assert.Equal(3m, withMusic.For("music", "suno-v6.0", null));

        // ×2 the 79AI cost, per model, 1 point = 1,000đ: 3,000 credits → 6,000đ = 6 points; 4,000 → 8 points.
        var doubled = prices with { Settings = prices.Settings with { MusicMarkup = 2m, MusicPoints = 3m } };
        Assert.Equal(6m, doubled.For("music", "suno-v5.5", null, 3000m));
        Assert.Equal(8m, doubled.For("music", "suno-v6.0", null, 4000m));

        // Images/videos never below 79AI cost × 2: a 27,200-credit/s model for 8 s is not sold at 0.8 point/s.
        Assert.Equal(435.2m, prices.For("video", "seedance_25_draft", "8", 217600m));
        Assert.Equal(0.5m, prices.For("image", "google_image_gen_3_5", null, 50m)); // cheap model: table 0.5 is above cost × 2 (0.1)
        Assert.Equal(6.4m, (prices with { Settings = prices.Settings with { CostFloorMarkup = null } }).For("video", "seedance_25_draft", "8", 217600m));
        Assert.Null(doubled.Missing("music", "suno-v5.5"));
        Assert.Null(new EditVideoAiPrices(new EditVideoAiSettings(), new Dictionary<string, decimal>()).For("video", "x", "8"));
    }

    [Fact]
    public void FallbackOrderComesFromSettingsAndCanBeTurnedOff()
    {
        var s = new EditVideoAiSettings { VideoFallback = "veo_omni, veo_3_1 ,veo_omni" };
        Assert.Equal(["veo_omni", "veo_3_1"], s.FallbackFor("video"));
        Assert.Empty((s with { FallbackEnabled = false }).FallbackFor("video"));
        Assert.Empty(s.FallbackFor("music"));
    }

    [Fact]
    public void MusicJobsAndSongLinksAreReadFromGommo()
    {
        var created = JsonNode.Parse("""{"success":true,"data":[{"id_base":"m1"},{"id_base":"m2"}]}""");
        Assert.Equal(["m1", "m2"], EditVideoGommoJson.IdBases(created));
        var done = JsonNode.Parse("""{"data":{"id_base":"m1","status":"SUCCESS","image_url":"https://cdn/x.jpg","audio_url":"https://cdn/song.mp3"}}""");
        Assert.Equal(["https://cdn/song.mp3"], EditVideoGommoJson.MusicOutputs(done));
        Assert.Equal(8, EditVideoGommoJson.Seconds("8s"));
        Assert.Equal(10, EditVideoGommoJson.Seconds("10 giây"));
        Assert.Null(EditVideoGommoJson.Seconds("auto"));
    }

    [Fact]
    public void VideoResultLinkPrefersResultUrlLikeTheWeb()
    {
        var task = JsonNode.Parse("""{"id_base":"v1","status":"SUCCESS","download_url":"https://cdn/d.mp4","result_url":"https://cdn/r.mp4"}""")!.AsObject();
        Assert.Equal(["https://cdn/r.mp4"], EditVideoGommoJson.Outputs(task, "video"));
    }

    [Fact]
    public void LicenseKeyOnScreenHidesItsMiddle()
    {
        const string key = "tdxv_zv5c3wz6t75fb8rrbs5n071scw41";
        var shown = EditVideoLicenseKeys.Mask(key);
        Assert.Equal(key.Length, shown.Length);
        Assert.StartsWith("tdxv_zv5", shown);
        Assert.EndsWith("cw41", shown);
        Assert.DoesNotContain("t75fb8rrbs5n071", shown);
        Assert.DoesNotContain("zv5c", EditVideoLicenseKeys.Mask("short"));
    }
}
