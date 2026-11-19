using Xunit;

namespace TodoX.Web.Tests;

/// <summary>Landing "TodoApp": the menu item replaces "EditVideo" and opens a page of app cards linking to each app's own page.</summary>
public sealed class LandingTodoAppPageTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, "TodoX.Landing", .. parts]));

    [Fact]
    public void Landing_menu_shows_TodoApp_instead_of_EditVideo()
    {
        foreach (var page in new[] { "index.html", "affiliate-video.html" })
        {
            var html = Read("wwwroot", page);
            Assert.Contains("<a href=\"/todo-app\">TodoApp</a>", html);
            Assert.DoesNotContain("<a href=\"/editvideo\">EditVideo</a>", html);
        }
    }

    [Fact]
    public void TodoApp_page_has_a_card_per_app_and_is_routed()
    {
        var html = Read("wwwroot", "todo-app.html");
        Assert.Contains("<a class=\"ta-app\" href=\"/editvideo\"", html);
        Assert.Contains("<a class=\"ta-app\" href=\"/reupvideo\"", html);
        Assert.Contains("app.MapGet(\"/todo-app\"", Read("Program.cs"));
        Assert.Contains("https://todox.vn/todo-app", Read("wwwroot", "sitemap.xml"));
    }
}
