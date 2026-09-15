using TodoX.Web.Models.Timelapse;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class TimelapseServiceSplitAndSceneUxTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void TimelapseServiceCatalog_ExposesSevenCategoryLockedServices()
    {
        var services = TimelapseServiceCatalog.Services;

        Assert.Equal(7, services.Count);
        Assert.Equal(
            new[]
            {
                "TIMELAPSE_CONSTRUCTION",
                "TIMELAPSE_LIVING_ROOM",
                "TIMELAPSE_BEDROOM",
                "TIMELAPSE_KITCHEN",
                "TIMELAPSE_POOL",
                "TIMELAPSE_INFRASTRUCTURE",
                "TIMELAPSE_LANDSCAPE"
            },
            services.Select(x => x.ServiceCode).ToArray());
        Assert.Equal(
            new[]
            {
                TimelapseServiceCatalog.ConstructionCategory,
                TimelapseServiceCatalog.LivingRoomCategory,
                TimelapseServiceCatalog.BedroomCategory,
                TimelapseServiceCatalog.KitchenCategory,
                TimelapseServiceCatalog.PoolCategory,
                TimelapseServiceCatalog.InfrastructureCategory,
                TimelapseServiceCatalog.LandscapeCategory
            },
            services.Select(x => x.Category).ToArray());
        Assert.All(services, x => Assert.False(string.IsNullOrWhiteSpace(x.CategoryLabel)));
        Assert.True(TimelapseServiceCatalog.TryGet("timelapse_landscape", out var landscape));
        Assert.Equal(TimelapseServiceCatalog.LandscapeCategory, landscape.Category);
    }

    [Fact]
    public void TimelapseCreatePage_FiltersProfilesBySelectedServiceCategory()
    {
        var create = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobCreate.razor");
        var repository = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProfileRepository.cs");

        Assert.Contains("TimelapseServiceCatalog.TryGet(service.ServiceCode, out var definition)", create, StringComparison.Ordinal);
        Assert.Contains("_request.ServiceCategory = definition.Category;", create, StringComparison.Ordinal);
        Assert.Contains("GetEnabledProfilesByCategoryAsync(_serviceDefinition.Category)", create, StringComparison.Ordinal);
        Assert.Contains("Cấu hình dựng", create, StringComparison.Ordinal);
        Assert.Contains("GetEnabledProfilesByCategoryAsync(string category", repository, StringComparison.Ordinal);
        Assert.Contains("GetEnabledProfileByCategoryAsync(string profileCode, string category", repository, StringComparison.Ordinal);
        Assert.Contains("GetRenderProfileByCategoryAsync(string profileCode, string category", repository, StringComparison.Ordinal);
        Assert.Contains("category AS Category", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelapseDetailPage_ShowsFloatingSceneActionAndPreStartEmptyStages()
    {
        var detail = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor");
        var css = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor.css");
        var workflow = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkflowService.cs");
        var jobService = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseJobService.cs");

        Assert.Contains("timelapse-floating-create", detail, StringComparison.Ordinal);
        Assert.Contains("StartIcon=\"@Icons.Material.Filled.PlayArrow\"", detail, StringComparison.Ordinal);
        Assert.Contains("CanShowFloatingRenderAction", detail, StringComparison.Ordinal);
        Assert.Contains("FloatingRenderActionLabel", detail, StringComparison.Ordinal);
        Assert.Contains("IsRenderNotStarted", detail, StringComparison.Ordinal);
        Assert.Contains("ResolveImageMediaState(image)", detail, StringComparison.Ordinal);
        Assert.Contains("Chờ tạo video", detail, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(3, minmax(0, 1fr));", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr));", css, StringComparison.Ordinal);
        Assert.Contains("position: fixed;", css, StringComparison.Ordinal);
        Assert.Contains("GetRenderProfileByCategoryAsync(snapshot.ProfileCode, snapshot.ServiceCategory", workflow, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_PROFILE_SERVICE_MISMATCH", jobService, StringComparison.Ordinal);
        Assert.Contains("ServiceCategory = serviceDefinition?.Category ?? profile.Category", jobService, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelapseDetailPage_KeepsImageMetadataInTwoGridChildrenAndShowsResumeActionForPartialJobs()
    {
        var detail = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor");
        var css = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor.css");

        Assert.Contains("CanShowFloatingRenderAction", detail, StringComparison.Ordinal);
        Assert.Contains("FloatingRenderActionLabel", detail, StringComparison.Ordinal);
        Assert.Contains("IsInitialFloatingRenderAction", detail, StringComparison.Ordinal);
        Assert.Contains("StartOrResumeAsync", detail, StringComparison.Ordinal);
        Assert.Contains("tl-image-meta-row", detail, StringComparison.Ordinal);
        Assert.Contains("tl-image-meta-actions", detail, StringComparison.Ordinal);
        Assert.Contains("tl-image-meta-copy", detail, StringComparison.Ordinal);
        Assert.Contains("image.Attempt > 0", detail, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: auto minmax(0, 1fr);", css, StringComparison.Ordinal);
        Assert.Contains(".tl-image-meta-actions", css, StringComparison.Ordinal);
        Assert.Contains("width: 100%;", css, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: break-word;", css, StringComparison.Ordinal);
        Assert.Contains("white-space: normal;", css, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowFloatingCreateVideoButton", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("IsRenderActive", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("grid-template-columns: 36px minmax(0, 1fr);", css, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelapseManualSql_SeedsSplitServicesAndDisablesLegacyConstructionVideo()
    {
        var sql = ReadSource("TodoX.Web", "database", "manual", "timelapse", "20260828_split_timelapse_services.sql");

        Assert.Contains("TIMELAPSE_CONSTRUCTION", sql, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_LIVING_ROOM", sql, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_BEDROOM", sql, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_KITCHEN", sql, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_POOL", sql, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_INFRASTRUCTURE", sql, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_LANDSCAPE", sql, StringComparison.Ordinal);
        Assert.Contains("service_code LIKE 'TIMELAPSE_%' OR service_code = 'CONSTRUCTION_VIDEO'", sql, StringComparison.Ordinal);
        Assert.Contains("lower(service_code) = 'construction_video'", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (service_code) DO UPDATE SET", sql, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] path)
        => File.ReadAllText(Path.Combine([RepositoryRoot, .. path]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TodoX.Dashboard.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find TodoX repository root.");
    }
}
