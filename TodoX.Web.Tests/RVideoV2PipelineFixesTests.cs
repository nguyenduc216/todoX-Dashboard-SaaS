using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoV2PipelineFixesTests
{
    [Fact]
    public void GeneratedPromptKeepsAspectRatioAndResolution()
    {
        var prompt = RVideoJobUpdateHelper.WithRenderSettings("""{"title":"Về quê","scenes":[{"scene":1}]}""", "9:16", "1080p");
        var parsed = new TodoXVideoPromptParser().Parse(prompt);
        Assert.Equal("9:16", parsed.Model.AspectRatio);
        Assert.Equal("1080p", parsed.Model.Resolution);
        Assert.Contains("Về quê", prompt);
        Assert.Single(parsed.Model.Scenes);
    }

    [Fact]
    public void EmptyPromptGetsASettingsObject()
        => Assert.Equal("720p", new TodoXVideoPromptParser().Parse(RVideoJobUpdateHelper.WithRenderSettings("", "9:16", "720p")).Model.Resolution);

    [Fact]
    public void RecentLinkFailureIsShownUntilALaterLinkStarts()
    {
        var now = new DateTime(2026, 11, 9, 5, 0, 0, DateTimeKind.Utc);
        var project = new VideoProjectDto();
        project.Events.Add(new VideoProjectEventDto { EventType = "RVIDEO_SCENE_LINK_FAILED", Level = "error", CreatedAt = now.AddMinutes(-2), DataJson = """{"error":"RVIDEO_LAST_FRAME_EMPTY"}""" });
        Assert.Contains("RVIDEO_LAST_FRAME_EMPTY", RVideoPipelineIssue.Latest(project, now));

        project.Events.Add(new VideoProjectEventDto { EventType = "RVIDEO_SCENE_LINK_STARTED", CreatedAt = now.AddMinutes(-1) });
        Assert.Null(RVideoPipelineIssue.Latest(project, now));
    }

    [Fact]
    public void OldFailuresAreNotShown()
    {
        var now = new DateTime(2026, 11, 9, 5, 0, 0, DateTimeKind.Utc);
        var project = new VideoProjectDto();
        project.Events.Add(new VideoProjectEventDto { EventType = "RVIDEO_AUTO_VALIDATION_FAILED", CreatedAt = now.AddHours(-2) });
        Assert.Null(RVideoPipelineIssue.Latest(project, now));
    }
}
