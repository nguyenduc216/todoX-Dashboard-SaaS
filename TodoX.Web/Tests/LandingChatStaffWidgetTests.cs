using Xunit;

namespace TodoX.Web.Tests;

public sealed class LandingChatStaffWidgetTests
{
    private const string WidgetScript = "https://chat-api.genstaff.net/widget/chatstaff-widget.js";

    [Theory]
    [InlineData("index.html")]
    [InlineData("video-ngan.html")]
    [InlineData("bat-dong-san.html")]
    [InlineData("editvideo.html")]
    [InlineData("mau-video.html")]
    public void LandingPageLoadsChatStaffWidgetOnceAsync(string page)
    {
        var html = File.ReadAllText(Path.Combine(FindLandingRoot(), "wwwroot", page));
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(html, System.Text.RegularExpressions.Regex.Escape(WidgetScript)).Count);
        Assert.Contains($"<script async src=\"{WidgetScript}\" data-channel-key=\"wlc_", html);
        Assert.True(html.IndexOf(WidgetScript, StringComparison.Ordinal) < html.IndexOf("</head>", StringComparison.Ordinal));
        // The MCP token reads customer conversations; it must never reach a public page.
        Assert.DoesNotContain("csmcp_", html);
    }

    [Fact]
    public void ContentSecurityPolicyAllowsChatStaffWidget()
    {
        var program = File.ReadAllText(Path.Combine(FindLandingRoot(), "Program.cs"));
        Assert.Contains("script-src 'self' https://chat-api.genstaff.net", program);
        Assert.Contains("connect-src 'self' https://chat-api.genstaff.net wss://chat-api.genstaff.net", program);
        Assert.Contains("frame-src https://chat-api.genstaff.net", program);
    }

    private static string FindLandingRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "TodoX.Landing");
            if (File.Exists(Path.Combine(candidate, "TodoX.Landing.csproj"))) return candidate;
        }
        throw new DirectoryNotFoundException("TodoX.Landing project not found.");
    }
}
