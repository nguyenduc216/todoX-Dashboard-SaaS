using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class FavoriteServicesRegressionTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string WebRoot = File.Exists(Path.Combine(RepoRoot, "Services", "AccountService.cs"))
        ? RepoRoot
        : Path.Combine(RepoRoot, "TodoX.Web");

    [Fact]
    public void CreatePage_UsesFavoriteCardAndRenamedCopy()
    {
        var source = Read("Components", "Pages", "Create.razor");
        Assert.Contains("@page \"/create\"", source);
        Assert.Contains("Kho dịch vụ video", source);
        Assert.Contains("<ServiceCatalogCard", source);
        Assert.Contains("ShowFavorite=\"true\"", source);
        Assert.Contains("OnFavoriteChanged=\"ToggleFavoriteAsync\"", source);
    }

    [Fact]
    public void CustomerDashboard_UsesFavoriteServicesOnly()
    {
        var source = Read("Components", "Pages", "CustomerDashboard.razor");
        Assert.Contains("GetFavoriteServicesAsync", source);
        Assert.DoesNotContain("GetActiveCatalogServicesAsync", source);
        Assert.Contains("<ServiceCatalogCard", source);
        Assert.Contains("Vào Kho dịch vụ video", source);
    }

    [Fact]
    public void AccountDialog_ShowsFavoriteChecklist()
    {
        var source = Read("Components", "Dialogs", "CustomerAccountDialog.razor");
        Assert.Contains("Dịch vụ hiển thị trên Dashboard", source);
        Assert.Contains("FavoriteServiceIds", source);
        Assert.Contains("MudCheckBox", source);
    }

    [Fact]
    public void MigrationScript_CreatesFavoriteRelationAndBackfillsActiveServices()
    {
        var source = Read("database", "manual", "customer-service-favorites", "20260901_customer_service_favorites.sql");
        Assert.Contains("crm.customer_service_favorites", source);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_service_favorites_tenant_user_service", source);
        Assert.Contains("ON CONFLICT (tenant_id, user_id, service_id) DO NOTHING", source);
        Assert.Contains("u.user_type = 'customer'", source);
        Assert.Contains("lower(s.status) = 'active'", source);
    }

    [Fact]
    public void AccountService_ExposesFavoriteOperations()
    {
        var source = Read("Services", "AccountService.cs");
        Assert.Contains("GetFavoriteServiceIdsAsync", source);
        Assert.Contains("GetFavoriteServicesAsync", source);
        Assert.Contains("SetCustomerAccountFavoritesAsync", source);
        Assert.Contains("ToggleFavoriteAsync", source);
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { WebRoot }.Concat(parts).ToArray()), Encoding.UTF8);
}
