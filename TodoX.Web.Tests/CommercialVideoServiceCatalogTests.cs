using TodoX.Web.Models.Catalog;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class CommercialVideoServiceCatalogTests
{
    [Fact]
    public void CommercialCatalog_SeedsTenCustomerFacingServices()
    {
        var services = CommercialVideoServiceCatalog.Services;

        Assert.Equal(10, services.Count);
        Assert.Equal(services.Count, services.Select(x => x.ServiceCode).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(services, x => FixedTodoXServiceCatalog.IsFixedServiceCode(x.ServiceCode));
        Assert.Contains(services, x => x.ServiceCode == "CONSTRUCTION_VIDEO" && x.EngineType == TodoXServiceEngineTypes.Timelapse);
        Assert.Contains(services, x => x.EngineType == TodoXServiceEngineTypes.RVideo);
        Assert.Contains(services, x => x.EngineType == TodoXServiceEngineTypes.RDance);
        Assert.Contains(services.GroupBy(x => x.EngineType, StringComparer.OrdinalIgnoreCase), x => x.Count() > 1);
        Assert.Equal(Enumerable.Range(1, 10).Select(x => x * 10), services.Select(x => x.SortOrder));
    }

    [Fact]
    public void EngineTypeContract_AllowsOnlyInternalEngines()
    {
        Assert.Equal([TodoXServiceEngineTypes.Timelapse, TodoXServiceEngineTypes.RVideo, TodoXServiceEngineTypes.RDance], TodoXServiceEngineTypes.All.Select(x => x.Value));
        Assert.True(TodoXServiceEngineTypes.IsValid("timelapse"));
        Assert.True(TodoXServiceEngineTypes.IsValid("RVIDEO"));
        Assert.False(TodoXServiceEngineTypes.IsValid("CONSTRUCTION_VIDEO"));
        Assert.Equal("rvideo", TodoXServiceEngineTypes.Normalize("RVIDEO"));
    }

    [Fact]
    public void BootstrapSellPrices_CoverImageAndVideoQualityDurationDefaults()
    {
        var prices = CommercialVideoServiceCatalog.BootstrapSellPrices;

        Assert.Equal(8, prices.Count);
        Assert.Contains(prices, x => x.AssetType == ServiceSellPriceAssetTypes.Image && x.QualityTier == ServiceSellPriceQualityTiers.Standard && x.DurationSeconds is null && x.SellPoints == 3);
        Assert.Contains(prices, x => x.AssetType == ServiceSellPriceAssetTypes.Image && x.QualityTier == ServiceSellPriceQualityTiers.Premium && x.DurationSeconds is null && x.SellPoints == 5);
        Assert.Contains(prices, x => x.AssetType == ServiceSellPriceAssetTypes.VideoScene && x.QualityTier == ServiceSellPriceQualityTiers.Standard && x.DurationSeconds == 6 && x.SellPoints == 10);
        Assert.Contains(prices, x => x.AssetType == ServiceSellPriceAssetTypes.VideoScene && x.QualityTier == ServiceSellPriceQualityTiers.Premium && x.DurationSeconds == 8 && x.SellPoints == 18);
    }

    [Fact]
    public void SellPriceRules_RejectInvalidRows()
    {
        ServiceSellPriceRules.Validate(new ServiceSellPriceDto
        {
            ServiceId = Guid.NewGuid(),
            AssetType = ServiceSellPriceAssetTypes.Image,
            QualityTier = ServiceSellPriceQualityTiers.Standard,
            SellPoints = 3
        });

        Assert.Throws<InvalidOperationException>(() => ServiceSellPriceRules.Validate(new ServiceSellPriceDto
        {
            ServiceId = Guid.NewGuid(),
            AssetType = ServiceSellPriceAssetTypes.Image,
            QualityTier = ServiceSellPriceQualityTiers.Standard,
            DurationSeconds = 4,
            SellPoints = 3
        }));

        Assert.Throws<InvalidOperationException>(() => ServiceSellPriceRules.Validate(new ServiceSellPriceDto
        {
            ServiceId = Guid.NewGuid(),
            AssetType = ServiceSellPriceAssetTypes.VideoScene,
            QualityTier = ServiceSellPriceQualityTiers.Standard,
            SellPoints = -1
        }));
    }

    [Fact]
    public void CustomerAndAdminSourceContracts_DoNotUseFixedThreeServiceCustomerFilter()
    {
        var create = ReadSource("TodoX.Web", "Components", "Pages", "Create.razor");
        var adminRepo = ReadSource("TodoX.Web", "Services", "CatalogAdminRepository.cs");
        var adminDialog = ReadSource("TodoX.Web", "Components", "Dialogs", "ServiceDialog.razor");
        var servicesPage = ReadSource("TodoX.Web", "Components", "Pages", "Services.razor");

        Assert.DoesNotContain("FixedTodoXServiceCatalog.IsFixedServiceCode", create, StringComparison.Ordinal);
        Assert.Contains("GetActiveCatalogServicesAsync", create, StringComparison.Ordinal);
        Assert.Contains("OrderBy(x => x.SortOrder)", create, StringComparison.Ordinal);
        Assert.Contains("CustomerServiceRouting.Resolve(service.ServiceType, service.Id, service.ServiceCode)", create, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyFixedDefinition", adminRepo, StringComparison.Ordinal);
        Assert.Contains("TodoXServiceEngineTypes.Normalize(s.ServiceType)", adminRepo, StringComparison.Ordinal);
        Assert.Contains("@bind-Value=\"_model.ServiceName\"", adminDialog, StringComparison.Ordinal);
        Assert.Contains("@bind-Value=\"_model.ServiceType\"", adminDialog, StringComparison.Ordinal);
        Assert.Contains("@bind-Value=\"_model.SortOrder\"", adminDialog, StringComparison.Ordinal);
        Assert.Contains("ServiceSellPricesDialog", servicesPage, StringComparison.Ordinal);
    }

    [Fact]
    public void SellPricingSourceContracts_KeepCustomerPriceSeparateFromProviderModelCost()
    {
        var catalogRepository = ReadSource("TodoX.Web", "Services", "CatalogRepository.cs");
        var adminRepository = ReadSource("TodoX.Web", "Services", "CatalogAdminRepository.cs");
        var resolver = ReadSource("TodoX.Web", "Services", "ServiceSellPriceResolver.cs");
        var migration = ReadSource("database", "migrations", "20260813_commercial_video_service_catalog.sql");

        Assert.Contains("catalog.service_sell_prices", catalogRepository, StringComparison.Ordinal);
        Assert.Contains("catalog.service_sell_prices", adminRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("todox_ai_model_price", resolver, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS catalog.service_sell_prices", migration, StringComparison.Ordinal);
        Assert.Contains("CHECK (asset_type IN ('image','video_scene'))", migration, StringComparison.Ordinal);
        Assert.Contains("CHECK (quality_tier IN ('standard','premium'))", migration, StringComparison.Ordinal);
        Assert.Contains("CHECK (sell_points >= 0)", migration, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (service_id, asset_type, quality_tier, (COALESCE(duration_seconds, 0)))", migration, StringComparison.Ordinal);
        Assert.Contains("DO UPDATE SET", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("sell_points = EXCLUDED.sell_points", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelapseCreator_CarriesSelectedCommercialServiceIdentity()
    {
        var page = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobCreate.razor");
        var service = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseJobService.cs");

        Assert.Contains("serviceId", page, StringComparison.Ordinal);
        Assert.Contains("serviceCode", page, StringComparison.Ordinal);
        Assert.Contains("request.ServiceId", service, StringComparison.Ordinal);
        Assert.Contains("request.ServiceCode", service, StringComparison.Ordinal);
        Assert.Contains("ServiceCode = service.ServiceCode", service, StringComparison.Ordinal);
        Assert.Contains("TodoXServiceEngineTypes.Timelapse", service, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TodoX.Dashboard.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
