using System.Reflection;
using TodoX.Web.Components.Pages;
using TodoX.Web.Models;
using Xunit;
namespace TodoX.Web.Tests;
public sealed class RVideoViewStateTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    [Fact]
    public async Task LiveRefreshPreservesProjectSceneDraftTabAndPlayingFinal()
    {
        await using var page=new RenderVideoJobs();
        var scene=new VideoProjectSceneDto {Id=42,VideoPrompt="draft prompt",Status="video_ready"};
        var project=new VideoProjectDto {Id=7,Scenes=[scene],FinalVideoUrl="/old.mp4"};
        typeof(RenderVideoJobs).GetField("_project",Flags)!.SetValue(page,project);
        typeof(RenderVideoJobs).GetField("_tabIndex",Flags)!.SetValue(page,2);
        Assert.Equal("/old.mp4",typeof(RenderVideoJobs).GetProperty("DisplayedFinalVideoUrl",Flags)!.GetValue(page));
        typeof(RenderVideoJobs).GetMethod("ApplyLiveProject",Flags)!.Invoke(page,[new VideoProjectDto {
            Id=7,Status="merging",FinalVideoUrl="/new.mp4",Scenes=[new() {Id=42,Status="video_rendering",VideoPrompt="server"}] }]);
        Assert.Same(project,typeof(RenderVideoJobs).GetField("_project",Flags)!.GetValue(page));
        Assert.Same(scene,project.Scenes[0]);
        Assert.Equal("draft prompt",scene.VideoPrompt);
        Assert.Equal("video_rendering",scene.Status);
        Assert.Equal(2,typeof(RenderVideoJobs).GetField("_tabIndex",Flags)!.GetValue(page));
        Assert.Equal("/old.mp4",typeof(RenderVideoJobs).GetProperty("DisplayedFinalVideoUrl",Flags)!.GetValue(page));
        typeof(RenderVideoJobs).GetMethod("ShowLatestFinalVideo",Flags)!.Invoke(page,[]);
        Assert.Equal("/new.mp4",typeof(RenderVideoJobs).GetProperty("DisplayedFinalVideoUrl",Flags)!.GetValue(page));
    }
    [Fact]
    public async Task LateResponseFromAnotherProjectCannotReplaceCurrentView()
    {
        await using var page=new RenderVideoJobs();
        var project=new VideoProjectDto {Id=8,Status="completed"};
        typeof(RenderVideoJobs).GetField("_project",Flags)!.SetValue(page,project);
        typeof(RenderVideoJobs).GetMethod("ApplyLiveProject",Flags)!.Invoke(page,[new VideoProjectDto {Id=7,Status="failed"}]);
        Assert.Equal("completed",project.Status);
    }
}
