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
        var dialog = ReadDialog();

        Assert.Contains("Giá vốn Provider", page, StringComparison.Ordinal);
        Assert.Contains("Giá vốn Provider", dialog, StringComparison.Ordinal);
        Assert.Contains("Không phải giá bán dịch vụ cho khách hàng.", page, StringComparison.Ordinal);
        Assert.Contains("Giá bán khách hàng được cấu hình theo Dịch vụ, không theo model provider.", dialog, StringComparison.Ordinal);
        Assert.Contains("Legacy sell pricing", page, StringComparison.Ordinal);
        Assert.Contains("Legacy sell pricing", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("MudTabPanel Text=\"GIÁ & ĐIỂM\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void QualityLabelHelperMapsProviderResolutionForDisplayOnly()
    {
        var dialog = ReadDialog();

        Assert.Contains("normalized.Equals(\"720p\", StringComparison.OrdinalIgnoreCase) ? \"Thường\"", dialog, StringComparison.Ordinal);
        Assert.Contains("normalized.Equals(\"1080p\", StringComparison.OrdinalIgnoreCase) ? \"Cao\"", dialog, StringComparison.Ordinal);
        Assert.Contains("normalized.Equals(\"2K\", StringComparison.OrdinalIgnoreCase) ? \"Thường\"", dialog, StringComparison.Ordinal);
        Assert.Contains("normalized.Equals(\"4K\", StringComparison.OrdinalIgnoreCase) ? \"Cao\"", dialog, StringComparison.Ordinal);
        Assert.Contains("GetQualityLabel(_model.MediaType, variant.Resolution)", dialog, StringComparison.Ordinal);
        Assert.Contains("GetQualityLabel(_model.MediaType, price.Resolution)", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalProviderValuesRemainVisibleFor79AiCatalogModels()
    {
        var dialog = ReadDialog();

        Assert.Contains("@variant.Resolution", dialog, StringComparison.Ordinal);
        Assert.Contains("DisplayValue(price.Resolution)", dialog, StringComparison.Ordinal);
        Assert.Contains("@FormatStrings(_model.SupportedResolutions)", dialog, StringComparison.Ordinal);
        Assert.Contains("@FormatDurations(_model.SupportedDurations)", dialog, StringComparison.Ordinal);
        Assert.Contains("@FormatStrings(_model.SupportedModes)", dialog, StringComparison.Ordinal);
        Assert.Contains("@FormatStrings(_model.SupportedRatios)", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelDetailUsesMudDialogAndInlineDetailIsRemoved()
    {
        var page = ReadPage();
        var dialog = ReadDialog();

        Assert.Contains("DialogService.ShowAsync<AiProviderModelDetailDialog>", page, StringComparison.Ordinal);
        Assert.Contains("MaxWidth = MaxWidth.ExtraLarge", page, StringComparison.Ordinal);
        Assert.Contains("FullWidth = true", page, StringComparison.Ordinal);
        Assert.Contains("OnRowClick=\"@(args => args.Item is not null ? OpenModelDialogAsync(args.Item) : Task.CompletedTask)\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@if (_selectedModel is not null)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<MudText Typo=\"Typo.subtitle2\">Supported variants</MudText>", page, StringComparison.Ordinal);
        Assert.Contains("<MudDialog Class=\"ai-provider-model-dialog\">", dialog, StringComparison.Ordinal);
        Assert.Contains("MudTabPanel Text=\"TỔNG QUAN\"", dialog, StringComparison.Ordinal);
        Assert.Contains("MudTabPanel Text=\"VARIANT\"", dialog, StringComparison.Ordinal);
        Assert.Contains("MudTabPanel Text=\"GIÁ VỐN\"", dialog, StringComparison.Ordinal);
        Assert.Contains("MudTabPanel Text=\"RAW / NÂNG CAO\"", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingFunctionalityRemainsAccessibleFromTabs()
    {
        var page = ReadPage();
        var dialog = ReadDialog();

        foreach (var handler in new[]
        {
            "SaveQuickDefaultsAsync",
            "SaveProvider",
            "SaveCapability",
            "SetDefault",
            "ReloadModelsAsync",
            "OpenModelDialogAsync",
            "ToggleModelAsync",
            "SyncSelectedProviderAsync",
            "EstimateAsync",
            "Migrate79AiCredentialAsync"
        })
        {
            Assert.Contains(handler, page, StringComparison.Ordinal);
        }

        Assert.Contains("SaveModelAsync", dialog, StringComparison.Ordinal);
        Assert.Contains("SavePriceAsync", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void PageAndModelDialogDoNotDisplaySecureTokenValues()
    {
        var page = ReadPage();
        var dialog = ReadDialog();

        Assert.Contains("MaskedHint", page, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", dialog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer ", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer ", dialog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", dialog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProviderSyncedFieldsAreReadonlyAndOnlyAdminFieldsRemainEditable()
    {
        var dialog = ReadDialog();

        foreach (var field in new[]
        {
            "_model.ProviderModelCode",
            "_model.MediaType",
            "_model.ServerCode",
            "_model.ProviderStatus",
            "_model.StatusMessage",
            "_model.BaseProviderPrice",
            "_model.ProviderPriceUnit",
            "_model.Source",
            "_model.LastProviderSyncAt"
        })
        {
            Assert.DoesNotContain($"@bind-Value=\"{field}\"", dialog, StringComparison.Ordinal);
        }

        foreach (var field in new[]
        {
            "_model.DisplayName",
            "_model.Enabled",
            "_model.AllowUserSelect",
            "_model.Description"
        })
        {
            Assert.Contains($"@bind-Value=\"{field}\"", dialog, StringComparison.Ordinal);
        }

        Assert.Contains("Value=\"_model.ProviderModelCode\"", dialog, StringComparison.Ordinal);
        Assert.Contains("Value=\"_model.MediaType\"", dialog, StringComparison.Ordinal);
        Assert.Contains("Value=\"_model.ServerCode\"", dialog, StringComparison.Ordinal);
        Assert.Contains("Value=\"_model.ProviderStatus\"", dialog, StringComparison.Ordinal);
        Assert.Contains("Value=\"_model.StatusMessage\"", dialog, StringComparison.Ordinal);
        Assert.Contains("Value=\"_model.BaseProviderPrice\"", dialog, StringComparison.Ordinal);
        Assert.Contains("Value=\"_model.ProviderPriceUnit\"", dialog, StringComparison.Ordinal);
        Assert.Contains("Value=\"_model.Source\"", dialog, StringComparison.Ordinal);
        Assert.Contains("Disabled=\"true\"", dialog, StringComparison.Ordinal);
        Assert.Contains("@bind-Value=\"_model.IsDeprecated\"", dialog, StringComparison.Ordinal);
    }

    private static string ReadPage()
    {
        var file = Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "AiProviders.razor");
        Assert.True(File.Exists(file), $"Missing file: {file}");
        return ReadStrictUtf8(file);
    }

    private static string ReadDialog()
    {
        var file = Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Dialogs", "AiProviderModelDetailDialog.razor");
        Assert.True(File.Exists(file), $"Missing file: {file}");
        return ReadStrictUtf8(file);
    }

    private static string ReadStrictUtf8(string file)
        => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetString(File.ReadAllBytes(file));

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
