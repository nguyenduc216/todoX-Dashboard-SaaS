using Xunit;

namespace TodoX.Web.Tests;

/// <summary>Service list cards (Tạo video, Dashboard) no longer show point prices; prices live inside each service.</summary>
public sealed class ServiceCardHidesPointsTests
{
    [Fact]
    public void ServiceCardsShowNoPointsUnlessAskedTo()
    {
        var card = File.ReadAllText(Path.Combine(WebRoot(), "Components", "Shared", "ServiceCatalogCard.razor"));

        Assert.Contains("public bool ShowPrice { get; set; }\n", card.Replace("\r\n", "\n"));
        Assert.DoesNotContain("Chưa có giá khởi điểm", card);

        foreach (var page in new[] { "Create.razor", "CustomerDashboard.razor" })
        {
            Assert.DoesNotContain("ShowPrice=\"true\"", File.ReadAllText(Path.Combine(WebRoot(), "Components", "Pages", page)));
        }
    }

    private static string WebRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "TodoX.Web");
            if (File.Exists(Path.Combine(candidate, "TodoX.Web.csproj")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("TodoX.Web project not found.");
    }
}
