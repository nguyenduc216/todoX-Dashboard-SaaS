using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Models.Timelapse;
using Xunit;

namespace TodoX.Web.Tests;

public class TimelapsePhase2ATests
{
    [Theory]
    [InlineData(TodoXServiceEngineTypes.Timelapse, CustomerServiceDestination.TimelapseCreator, "/jobs/timelapse/new")]
    [InlineData(TodoXServiceEngineTypes.RVideo, CustomerServiceDestination.RVideoCreator, null)]
    [InlineData(TodoXServiceEngineTypes.RDance, CustomerServiceDestination.RDanceCreator, "/rdance-fashion-demo")]
    public void CustomerServiceRouting_UsesEngineType(
        string engineType,
        CustomerServiceDestination expectedDestination,
        string? expectedRoute)
    {
        var serviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var route = CustomerServiceRouting.Resolve(engineType, serviceId, "CONSTRUCTION_VIDEO");

        Assert.Equal(expectedDestination, route.Destination);
        if (expectedRoute is null)
        {
            Assert.Null(route.Route);
        }
        else
        {
            Assert.StartsWith(expectedRoute, route.Route);
            if (route.Route.Contains("?", StringComparison.Ordinal))
            {
                Assert.Contains($"serviceId={serviceId}", route.Route);
                Assert.Contains("serviceCode=CONSTRUCTION_VIDEO", route.Route);
            }
        }

        Assert.NotEqual("Dịch vụ RDance đang hoàn thiện.", route.Message);
    }

    [Theory]
    [InlineData(3, new[] { 0, 35, 70, 100 })]
    [InlineData(4, new[] { 0, 25, 50, 75, 100 })]
    [InlineData(5, new[] { 0, 20, 40, 60, 80, 100 })]
    [InlineData(6, new[] { 0, 25, 40, 55, 70, 75, 90, 100 })]
    public void ProgressMappings_AreFixedBySceneCount(int sceneCount, int[] expected)
    {
        Assert.Equal(expected, TimelapseRequestRules.GetProgressMapping(sceneCount));
    }

    [Fact]
    public void TimelapseRequest_RejectsUnsupportedInputs()
    {
        var errors = TimelapseRequestRules.Validate(
            new TimelapseCreateRequest
            {
                ProfileCode = string.Empty,
                SceneCount = 7,
                VideoMode = "provider_mode",
                Ratio = "1_1"
            },
            hasOriginalImage: false);

        Assert.Equal(5, errors.Count);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void TimelapseRequest_AcceptsOnlyConfiguredSceneCounts(int sceneCount)
    {
        var errors = TimelapseRequestRules.Validate(
            new TimelapseCreateRequest
            {
                ProfileCode = "from_database",
                SceneCount = sceneCount,
                VideoMode = TimelapseRequestRules.FastMode,
                Ratio = TimelapseRequestRules.LandscapeRatio
            },
            hasOriginalImage: true);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(TimelapseRequestRules.FastMode, ServiceSellPriceQualityTiers.Standard, "Tiêu chuẩn")]
    [InlineData(TimelapseRequestRules.ProfessionalMode, ServiceSellPriceQualityTiers.Premium, "Cao cấp")]
    public void TimelapseSellPricing_MapsRuntimeModesToCustomerQualityTiers(string mode, string qualityTier, string label)
    {
        Assert.Equal(qualityTier, TimelapseSellPricing.QualityTierForMode(mode));
        Assert.Equal(label, TimelapseSellPricing.CustomerQualityLabel(mode));
    }

    [Theory]
    [InlineData(3, 10, 30)]
    [InlineData(4, 10, 40)]
    [InlineData(5, 10, 50)]
    [InlineData(6, 10, 60)]
    public void TimelapseSellPricing_MultipliesVideoScenePriceBySceneCount(int sceneCount, decimal price, decimal expected)
    {
        Assert.Equal(expected, TimelapseSellPricing.EstimateVideoSubtotal(price, sceneCount));
    }

    [Fact]
    public void TimelapseCustomerCreator_UsesSelectedServiceAndSellPriceContracts()
    {
        var page = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobCreate.razor");
        var service = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseJobService.cs");
        var catalog = ReadSource("TodoX.Web", "Services", "CatalogRepository.cs");
        var adminCatalog = ReadSource("TodoX.Web", "Services", "CatalogAdminRepository.cs");
        var models = ReadSource("TodoX.Web", "Models", "Timelapse", "TimelapseModels.cs");
        var resolver = ReadSource("TodoX.Web", "Services", "ServiceSellPriceResolver.cs");

        Assert.Contains("<PageTitle>@PageHeading</PageTitle>", page, StringComparison.Ordinal);
        Assert.Contains("Catalog.GetServiceByIdAsync(_request.ServiceId.Value)", page, StringComparison.Ordinal);
        Assert.Contains("Dịch vụ này đang tạm ngưng", page, StringComparison.Ordinal);
        Assert.Contains("Dịch vụ đã chọn không thuộc nhóm Timelapse", page, StringComparison.Ordinal);
        Assert.Contains("timelapse-upload-card", page, StringComparison.Ordinal);
        Assert.Contains("Chọn ảnh thành phẩm", page, StringComparison.Ordinal);
        Assert.Contains("Bấm để tải ảnh lên", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Kéo thả ảnh vào đây", page, StringComparison.Ordinal);
        Assert.Contains("JPG, PNG hoặc WebP · tối đa 10MB", page, StringComparison.Ordinal);
        Assert.Contains("AllowedImageContentTypes", page, StringComparison.Ordinal);
        Assert.Contains("MaxImageBytes", page, StringComparison.Ordinal);
        Assert.Contains("ImageCount: 0", page, StringComparison.Ordinal);
        Assert.Contains("TimelapseSellPricing.QualityTierForMode(_request.VideoMode)", page, StringComparison.Ordinal);
        Assert.Contains("TimelapseRequestRules.RuntimeClipDurationSeconds", page, StringComparison.Ordinal);
        Assert.Contains("SellPrices.EstimateAsync", page, StringComparison.Ordinal);
        Assert.Contains("SubmitDisabled", page, StringComparison.Ordinal);
        Assert.Contains("_priceLoading", page, StringComparison.Ordinal);
        Assert.Contains("!_request.ServiceId.HasValue || _request.ServiceId.Value == Guid.Empty", page, StringComparison.Ordinal);
        Assert.Contains("!HasValidPrice", page, StringComparison.Ordinal);
        Assert.Contains("Bắt đầu tạo video", page, StringComparison.Ordinal);
        Assert.Contains("TIÊU CHUẨN", page, StringComparison.Ordinal);
        Assert.Contains("CAO CẤP", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<MudText Typo=\"Typo.h4\" Class=\"todox-page-title\">Video Timelapse AI</MudText>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Fast</MudRadio>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Professional</MudRadio>", page, StringComparison.Ordinal);

        Assert.Contains("GetServiceByIdAsync", catalog, StringComparison.Ordinal);
        Assert.Contains("WHERE s.id = @serviceId", catalog, StringComparison.Ordinal);
        Assert.Contains("CASE WHEN lower(s.status) = 'active'", catalog, StringComparison.Ordinal);
        Assert.Contains("IServiceSellPriceResolver", service, StringComparison.Ordinal);
        Assert.Contains("!request.ServiceId.HasValue || request.ServiceId.Value == Guid.Empty", service, StringComparison.Ordinal);
        Assert.Contains("_catalog.GetServiceByIdAsync(request.ServiceId.Value, ct)", service, StringComparison.Ordinal);
        Assert.Contains("Dịch vụ đã chọn không tồn tại.", service, StringComparison.Ordinal);
        Assert.Contains("Dịch vụ này đang tạm ngưng.", service, StringComparison.Ordinal);
        Assert.Contains("Dịch vụ đã chọn không thuộc nhóm Timelapse.", service, StringComparison.Ordinal);
        Assert.Contains("Dịch vụ đã chọn không khớp với mã dịch vụ.", service, StringComparison.Ordinal);
        Assert.Contains("_sellPrices.ResolveVideoScenePriceAsync", service, StringComparison.Ordinal);
        Assert.Contains("TimelapseSellPricing.QualityTierForMode(request.VideoMode)", service, StringComparison.Ordinal);
        Assert.Contains("TimelapseRequestRules.RuntimeClipDurationSeconds", service, StringComparison.Ordinal);
        Assert.Contains("TimelapseSellPriceSnapshot", service, StringComparison.Ordinal);
        Assert.Contains("TotalPoints = videoSubtotal", service, StringComparison.Ordinal);
        Assert.Contains("catalog.service_sell_prices", adminCatalog, StringComparison.Ordinal);
        Assert.DoesNotContain("todox_ai_model_price", resolver, StringComparison.Ordinal);
        Assert.Contains("public const int RuntimeClipDurationSeconds = 6", models, StringComparison.Ordinal);
        Assert.Contains("AllowedSceneCounts { get; } = [3, 4, 5, 6]", models, StringComparison.Ordinal);
        Assert.Contains("FastMode = \"fast\"", models, StringComparison.Ordinal);
        Assert.Contains("ProfessionalMode = \"professional\"", models, StringComparison.Ordinal);
        Assert.Contains("LandscapeRatio = \"16_9\"", models, StringComparison.Ordinal);
        Assert.Contains("PortraitRatio = \"9_16\"", models, StringComparison.Ordinal);
        Assert.DoesNotContain("GetActiveCatalogServicesAsync()", service, StringComparison.Ordinal);
        Assert.DoesNotContain("timelapseServices.FirstOrDefault", service, StringComparison.Ordinal);
        Assert.DoesNotContain("SingleOrDefault(x => string.Equals(x.ServiceCode", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Loáº¡i", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Vui lÃ²ng", models, StringComparison.Ordinal);
        Assert.DoesNotContain("Sá»‘", models, StringComparison.Ordinal);
        Assert.DoesNotContain("Cháº¿", models, StringComparison.Ordinal);
        Assert.DoesNotContain("Tá»·", models, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelapseJobAccess_DeniesAnotherCustomersJob()
    {
        var ownerUserId = Guid.NewGuid();
        var ownerCustomerId = Guid.NewGuid();
        var otherCustomer = new CurrentUserSession
        {
            UserId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            IsAuthenticated = true,
            Role = TodoXUserRole.CustomerOwner
        };

        Assert.False(TimelapseJobAccess.CanRead(ownerUserId, ownerCustomerId, otherCustomer));
    }

    [Fact]
    public void TimelapseJobAccess_AllowsTheOwningCustomerUser()
    {
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var currentUser = new CurrentUserSession
        {
            UserId = userId,
            CustomerId = customerId,
            IsAuthenticated = true,
            Role = TodoXUserRole.CustomerUser
        };

        Assert.True(TimelapseJobAccess.CanRead(userId, customerId, currentUser));
    }

    [Fact]
    public void TimelapseJobAccess_AllowsAnotherUserInSameCustomer()
    {
        var ownerUserId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var currentUser = new CurrentUserSession
        {
            UserId = Guid.NewGuid(),
            CustomerId = customerId,
            IsAuthenticated = true,
            Role = TodoXUserRole.CustomerUser
        };

        Assert.True(TimelapseJobAccess.CanRead(ownerUserId, customerId, currentUser));
    }

    [Fact]
    public void TimelapseJobAccess_DeniesCustomerSessionWithoutCustomerId()
    {
        var currentUser = new CurrentUserSession
        {
            UserId = Guid.NewGuid(),
            CustomerId = null,
            IsAuthenticated = true,
            Role = TodoXUserRole.CustomerUser
        };

        Assert.False(TimelapseJobAccess.CanRead(Guid.NewGuid(), Guid.NewGuid(), currentUser));
    }

    [Fact]
    public void TimelapseOwnership_SourceContracts_AlignCreateReadAndNavigation()
    {
        var createPage = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobCreate.razor");
        var detailPage = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor");
        var service = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseJobService.cs");
        var models = ReadSource("TodoX.Web", "Models", "Timelapse", "TimelapseModels.cs");

        Assert.Contains("Navigation.NavigateTo($\"/jobs/timelapse/{job.Id}\")", createPage, StringComparison.Ordinal);
        Assert.Contains("UserId = currentUser.UserId", service, StringComparison.Ordinal);
        Assert.Contains("CustomerId = currentUser.CustomerId", service, StringComparison.Ordinal);
        Assert.Contains("JobType = RenderJobTypes.Timelapse", service, StringComparison.Ordinal);

        Assert.Contains("SelectJobByIdSql", service, StringComparison.Ordinal);
        Assert.Contains("WHERE id=@jobId", service, StringComparison.Ordinal);
        Assert.Contains("row.TenantId != _tenant.TenantId", service, StringComparison.Ordinal);
        Assert.Contains("job_type AS JobType", service, StringComparison.Ordinal);
        Assert.Contains("customer_id IS NOT DISTINCT FROM @customerId", service, StringComparison.Ordinal);
        Assert.DoesNotContain("AND user_id=@userId", service, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_JOB_GET_OWNED_MISS", service, StringComparison.Ordinal);
        Assert.Contains("tenant_mismatch", service, StringComparison.Ordinal);
        Assert.Contains("job_type_mismatch", service, StringComparison.Ordinal);
        Assert.Contains("ownership_mismatch", service, StringComparison.Ordinal);

        Assert.Contains("jobCustomerId == currentUser.CustomerId", models, StringComparison.Ordinal);
        Assert.DoesNotContain("jobUserId == currentUser.UserId", models, StringComparison.Ordinal);

        Assert.Contains("Không tìm thấy job.", detailPage, StringComparison.Ordinal);
        Assert.Contains("Bạn không có quyền xem job này.", detailPage, StringComparison.Ordinal);
        Assert.Contains("Không thể tải thông tin job. Vui lòng thử lại.", detailPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Không tìm thấy job hoặc bạn không có quyền xem job này.", detailPage, StringComparison.Ordinal);
    }

    [Fact]
    public void FixedCatalog_RemainsLegacyOnlyTimelapseReference()
    {
        Assert.Equal("timelapse", FixedTodoXServiceCatalog.ResolveServiceType(FixedTodoXServiceCatalog.Timelapse));
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
