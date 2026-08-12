using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class AiProvidersExperienceTests
{
    [Fact]
    public void MainTabsExistInTheProviderAdminExperience()
    {
        var page = ReadPage();

        foreach (var tab in new[]
        {
            "TỔNG QUAN",
            "PROVIDER",
            "MODEL & VARIANT",
            "GIÁ VỐN",
            "MẶC ĐỊNH",
            "ĐỒNG BỘ",
            "NÂNG CAO"
        })
        {
            Assert.Contains($"MudTabPanel Text=\"{tab}\"", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProviderCostLanguageIsPrimaryAndSellPricingIsSeparated()
    {
        var page = ReadPage();

        Assert.Contains("Giá vốn Provider", page, StringComparison.Ordinal);
        Assert.Contains("Không phải giá bán dịch vụ cho khách hàng.", page, StringComparison.Ordinal);
        Assert.Contains("Giá bán khách hàng sẽ được cấu hình theo Dịch vụ, không theo model provider.", page, StringComparison.Ordinal);
        Assert.Contains("Legacy sell pricing", page, StringComparison.Ordinal);
        Assert.DoesNotContain("MudTabPanel Text=\"GIÁ & ĐIỂM\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void QualityLabelHelperMapsProviderResolutionForDisplayOnly()
    {
        var page = ReadPage();

        Assert.Contains("normalized.Equals(\"720p\", StringComparison.OrdinalIgnoreCase) ? \"Thường\"", page, StringComparison.Ordinal);
        Assert.Contains("normalized.Equals(\"1080p\", StringComparison.OrdinalIgnoreCase) ? \"Cao\"", page, StringComparison.Ordinal);
        Assert.Contains("normalized.Equals(\"2K\", StringComparison.OrdinalIgnoreCase) ? \"Thường\"", page, StringComparison.Ordinal);
        Assert.Contains("normalized.Equals(\"4K\", StringComparison.OrdinalIgnoreCase) ? \"Cao\"", page, StringComparison.Ordinal);
        Assert.Contains("GetQualityLabel(_selectedModel.MediaType, variant.Resolution)", page, StringComparison.Ordinal);
        Assert.Contains("GetQualityLabel(_selectedModel.MediaType, price.Resolution)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalProviderValuesRemainVisibleFor79AiCatalogModels()
    {
        var page = ReadPage();

        Assert.Contains("@variant.Resolution", page, StringComparison.Ordinal);
        Assert.Contains("DisplayValue(price.Resolution)", page, StringComparison.Ordinal);
        Assert.Contains("@FormatStrings(_selectedModel.SupportedResolutions)", page, StringComparison.Ordinal);
        Assert.Contains("@FormatDurations(_selectedModel.SupportedDurations)", page, StringComparison.Ordinal);
        Assert.Contains("@FormatStrings(_selectedModel.SupportedModes)", page, StringComparison.Ordinal);
        Assert.Contains("@FormatStrings(_selectedModel.SupportedRatios)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingFunctionalityRemainsAccessibleFromTabs()
    {
        var page = ReadPage();

        foreach (var handler in new[]
        {
            "SaveQuickDefaultsAsync",
            "SaveProvider",
            "SaveCapability",
            "SetDefault",
            "ReloadModelsAsync",
            "SaveSelectedModelAsync",
            "ToggleModelAsync",
            "SavePriceAsync",
            "SyncSelectedProviderAsync",
            "EstimateAsync",
            "Migrate79AiCredentialAsync"
        })
        {
            Assert.Contains(handler, page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PageDoesNotDisplaySecureTokenValues()
    {
        var page = ReadPage();

        Assert.Contains("MaskedHint", page, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer ", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", page, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadPage()
    {
        var file = Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "AiProviders.razor");
        Assert.True(File.Exists(file), $"Missing file: {file}");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetString(File.ReadAllBytes(file));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TodoX.Dashboard.sln"))
                && Directory.Exists(Path.Combine(dir.FullName, "TodoX.Web")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate todoX-Dashboard-SaaS repo root.");
    }
}
