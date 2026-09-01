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
    public void FavoriteAction_IsRenderedBesidePrimaryAction_NotOverThumbnail()
    {
        var source = Read("Components", "Shared", "ServiceCatalogCard.razor");
        var mediaStart = source.IndexOf("<div class=\"todox-service-media\">", StringComparison.Ordinal);
        var mediaEnd = source.IndexOf("</div>", mediaStart, StringComparison.Ordinal);
        var favoriteAction = source.IndexOf("Icons.Material.Filled.Favorite", StringComparison.Ordinal);
        var primaryAction = source.IndexOf("PrimaryActionLabel", StringComparison.Ordinal);

        Assert.True(mediaStart >= 0 && mediaEnd > mediaStart);
        Assert.True(favoriteAction > mediaEnd);
        Assert.True(primaryAction > favoriteAction);
        Assert.DoesNotContain("todox-service-favorite\"", source);
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
    public void MigrationScript_CreatesFavoriteRelationWithoutDefaultBackfill()
    {
        var source = Read("database", "manual", "customer-service-favorites", "20260901_customer_service_favorites.sql");
        Assert.Contains("crm.customer_service_favorites", source);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_service_favorites_tenant_user_service", source);
        Assert.Contains("Do NOT backfill active services", source);
        Assert.DoesNotContain("CROSS JOIN catalog.services", source);
        Assert.DoesNotContain("INSERT INTO crm.customer_service_favorites", source);
    }

    [Fact]
    public void CleanupScript_ClearsPreviouslyBackfilledFavorites()
    {
        var source = Read("database", "manual", "customer-service-favorites", "20260901_clear_all_customer_service_favorites.sql");
        Assert.Contains("DELETE FROM crm.customer_service_favorites", source);
        Assert.Contains("favorite_count", source);
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

    [Fact]
    public void AdminFavoriteMutation_IsProtectedByServiceAuthorization()
    {
        var source = Read("Services", "AccountService.cs");
        Assert.Contains("CanManageCustomerAccountFavorites(actor)", source);
        Assert.Contains("actor.IsAuthenticated", source);
        Assert.Contains("!actor.IsCustomer", source);
        Assert.Contains("actor.IsRoot", source);
        Assert.Contains("TodoXUserRole.Admin", source);
        Assert.Contains("TodoXUserRole.SystemOperator", source);
        Assert.Contains("actor.Can(\"customer_accounts.create\")", source);
        Assert.Contains("actor.Can(\"customer_accounts.update\")", source);
        Assert.Contains("Bạn không có quyền cập nhật dịch vụ hiển thị trên Dashboard", source);
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { WebRoot }.Concat(parts).ToArray()), Encoding.UTF8);
}
