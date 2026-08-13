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
    public void ServiceDialog_RestoresThumbnailManagementAndServiceCodeRules()
    {
        var adminDialog = ReadSource("TodoX.Web", "Components", "Dialogs", "ServiceDialog.razor");
        var adminRepo = ReadSource("TodoX.Web", "Services", "CatalogAdminRepository.cs");

        Assert.Contains("ReadOnly=\"@(!_isNew)\"", adminDialog, StringComparison.Ordinal);
        Assert.Contains("private bool _isNew;", adminDialog, StringComparison.Ordinal);
        Assert.Contains("service-thumbnail-preview", adminDialog, StringComparison.Ordinal);
        Assert.Contains("<img src=\"@_model.ThumbnailUrl\"", adminDialog, StringComparison.Ordinal);
        Assert.Contains("<InputFile OnChange=\"UploadThumbnail\" accept=\"image/png,image/jpeg,image/webp\"", adminDialog, StringComparison.Ordinal);
        Assert.Contains("SystemImageStorage.SaveServiceThumbnailAsync(file)", adminDialog, StringComparison.Ordinal);
        Assert.Contains("ServiceIllustrationRenderDialog", adminDialog, StringComparison.Ordinal);
        Assert.Contains("Nâng cao", adminDialog, StringComparison.Ordinal);
        Assert.Contains("Workflow reference", adminDialog, StringComparison.Ordinal);
        Assert.Contains("NormalizeServiceCode(s.ServiceCode)", adminRepo, StringComparison.Ordinal);
        Assert.Contains("ServiceCodeExistsAsync(s.ServiceCode)", adminRepo, StringComparison.Ordinal);
        Assert.Contains("char.IsAsciiLetterUpper(c) || char.IsDigit(c) || c == '_'", adminRepo, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerCatalogQuery_UsesServiceAliasForDynamicCatalogAndPriceSummary()
    {
        var catalogRepository = ReadSource("TodoX.Web", "Services", "CatalogRepository.cs");
        var createPage = ReadSource("TodoX.Web", "Components", "Pages", "Create.razor");

        Assert.Contains("FROM catalog.services s", catalogRepository, StringComparison.Ordinal);
        Assert.Contains("SELECT s.id AS Id", catalogRepository, StringComparison.Ordinal);
        Assert.Contains("s.service_code AS ServiceCode", catalogRepository, StringComparison.Ordinal);
        Assert.Contains("s.thumbnail_url AS ThumbnailUrl", catalogRepository, StringComparison.Ordinal);
        Assert.Contains("s.cover_image_url AS CoverImageUrl", catalogRepository, StringComparison.Ordinal);
        Assert.Contains("p.service_id = s.id", catalogRepository, StringComparison.Ordinal);
        Assert.Contains("string_agg(summary_text, ' · ' ORDER BY sort_key)", catalogRepository, StringComparison.Ordinal);
        Assert.Contains("Từ ' || min(p.sell_points)::text || ' điểm / hình", catalogRepository, StringComparison.Ordinal);
        Assert.Contains("Từ ' || min(p.sell_points)::text || ' điểm / scene", catalogRepository, StringComparison.Ordinal);
        Assert.Contains("WHERE lower(s.status) = 'active'", catalogRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE lower(s.status) IN ('enabled', 'active')", catalogRepository, StringComparison.Ordinal);
        Assert.Contains("ORDER BY s.sort_order, s.service_name", catalogRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM catalog.services\r\n", catalogRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM catalog.services\n", catalogRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("FixedTodoXServiceCatalog.Services.Take(3)", createPage, StringComparison.Ordinal);
        Assert.Contains("GetActiveCatalogServicesAsync", createPage, StringComparison.Ordinal);
        Assert.Contains("ThumbnailUrl", createPage, StringComparison.Ordinal);
        Assert.Contains("CoverImageUrl", createPage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("active", "active", true)]
    [InlineData("enabled", "active", true)]
    [InlineData("ACTIVE", "active", true)]
    [InlineData("inactive", "inactive", false)]
    [InlineData("disabled", "inactive", false)]
    [InlineData("", "inactive", false)]
    [InlineData(null, "inactive", false)]
    [InlineData("unknown", "inactive", false)]
    public void ServiceStatuses_NormalizeLegacyValuesToCanonicalContract(string? input, string expected, bool active)
    {
        Assert.Equal(expected, TodoXServiceStatuses.Normalize(input));
        Assert.Equal(active, TodoXServiceStatuses.IsActive(input));
    }

    [Fact]
    public void CommercialServiceStatusContracts_UseActiveInactiveForAdminAndCustomer()
    {
        var adminDialog = ReadSource("TodoX.Web", "Components", "Dialogs", "ServiceDialog.razor");
        var adminPage = ReadSource("TodoX.Web", "Components", "Pages", "Services.razor");
        var adminRepository = ReadSource("TodoX.Web", "Services", "CatalogAdminRepository.cs");
        var customerRepository = ReadSource("TodoX.Web", "Services", "CatalogRepository.cs");
        var statusMigration = ReadSource("database", "migrations", "20260813_catalog_service_status_active_inactive.sql");

        Assert.Contains("Status = TodoXServiceStatuses.Active", adminDialog, StringComparison.Ordinal);
        Assert.Contains("Value=\"@TodoXServiceStatuses.Active\">Hoạt động", adminDialog, StringComparison.Ordinal);
        Assert.Contains("Value=\"@TodoXServiceStatuses.Inactive\">Tạm ngưng", adminDialog, StringComparison.Ordinal);
        Assert.Contains("TodoXServiceStatuses.Normalize(Source.Status)", adminDialog, StringComparison.Ordinal);
        Assert.Contains("TodoXServiceStatuses.Normalize(s.Status)", adminRepository, StringComparison.Ordinal);
        Assert.Contains("TodoXServiceStatuses.LabelFor(svc.Status)", adminPage, StringComparison.Ordinal);
        Assert.Contains("TodoXServiceStatuses.IsActive(svc.Status) ? Color.Success : Color.Default", adminPage, StringComparison.Ordinal);
        Assert.Contains("ORDER BY s.sort_order, s.service_name", adminRepository, StringComparison.Ordinal);
        Assert.Contains("WHERE lower(s.status) = 'active'", customerRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("FixedTodoXServiceCatalog.IsEnabledStatus", adminPage, StringComparison.Ordinal);
        Assert.DoesNotContain("IN ('enabled', 'active')", customerRepository, StringComparison.Ordinal);
        Assert.Contains("SET status = 'active'", statusMigration, StringComparison.Ordinal);
        Assert.Contains("IN ('enabled', 'active')", statusMigration, StringComparison.Ordinal);
        Assert.Contains("SET status = 'inactive'", statusMigration, StringComparison.Ordinal);
        Assert.Contains("IN ('disabled', 'inactive')", statusMigration, StringComparison.Ordinal);
        Assert.Contains("lower(trim(status)) NOT IN ('active', 'inactive')", statusMigration, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN status SET DEFAULT 'active'", statusMigration, StringComparison.Ordinal);
        Assert.Contains("CHECK (status IN ('active', 'inactive'))", statusMigration, StringComparison.Ordinal);
        Assert.Equal(10, CommercialVideoServiceCatalog.Services.Count);
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
    public void ThumbnailManifestDocumentsDeterministicMappingWhenFilesAreUnavailable()
    {
        var manifest = ReadSource("TodoX.Web", "docs", "commercial-thumbnail-manifest.md");
        var migration = ReadSource("database", "migrations", "20260813_commercial_video_service_catalog.sql");

        foreach (var service in CommercialVideoServiceCatalog.Services)
        {
            Assert.Contains(service.ServiceCode, manifest, StringComparison.Ordinal);
            Assert.Contains(service.ThumbnailManifestKey, manifest, StringComparison.Ordinal);
        }

        Assert.Contains("thumbnail_url = COALESCE(NULLIF(catalog.services.thumbnail_url, ''), EXCLUDED.thumbnail_url)", migration, StringComparison.Ordinal);
        Assert.Contains("does not invent thumbnail URLs", migration, StringComparison.Ordinal);
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
