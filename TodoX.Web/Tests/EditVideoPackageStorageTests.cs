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
