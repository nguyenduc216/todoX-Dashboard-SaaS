using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoAutoRunStartTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    [Fact]
    public void Auto_mode_runs_only_after_the_user_starts_it()
    {
        var settings = new RVideoJobSettingsDto { ExecutionMode = RVideoExecutionModes.Auto };
        Assert.False(settings.AutoRunActive);
        settings.AutoRunStartedAt = DateTime.UtcNow;
        Assert.True(settings.AutoRunActive);
        settings.ExecutionMode = RVideoExecutionModes.Manual;
        Assert.False(settings.AutoRunActive);

        var worker = Read("TodoX.Web", "Services", "VideoRender", "RVideoLifecycleWorker.cs");
        Assert.Contains("&& setting.AutoRunStartedAt is not null;", worker);
        Assert.Contains("auto_run_started_at AS AutoRunStartedAt", worker);
    }

    [Fact]
    public void New_scenes_wait_for_a_new_start_and_the_start_buttons_exist()
    {
        var repository = Read("TodoX.Web", "Services", "VideoRender", "VideoRenderRepository.cs");
        Assert.Contains("SET auto_run_started_at=NULL WHERE project_id=@projectId", repository);
        // Leaving AUTO cancels the start, so returning to AUTO asks again.
        Assert.Contains("auto_run_started_at=CASE WHEN EXCLUDED.execution_mode='AUTO'", Read("TodoX.Web", "Services", "VideoRender", "RVideoJobSettingsRepository.cs"));
        Assert.Contains("Bắt đầu tạo video", Read("TodoX.Web", "Components", "Pages", "RVideo", "RVideoV2Info.razor"));
        Assert.Contains("ScenePipeline.StartAutoRunAsync(", Read("TodoX.Web", "Components", "Pages", "RVideo", "RVideoV2Preview.BulkRerender.cs"));
        Assert.True(File.Exists(Path.Combine(Root, "database", "manual", "rvideo", "20261111_rvideo_auto_run_start.test.sql")));
    }

    [Fact]
    public void Link_picker_shows_only_for_linked_video_jobs()
    {
        static VideoProjectSceneDto Scene(int index, string? mode)
        {
            var scene = new VideoProjectSceneDto { Id = index, SceneIndex = index };
            if (mode is not null)
            {
                var metadata = ScenePromptMetadata.FromScene(scene);
                metadata.Extra[RVideoSceneLink.ModeKey] = mode;
                scene.ScenePrompt = metadata.Serialize();
            }
            return scene;
        }

        Assert.False(RVideoSceneLink.IsProjectLinked([Scene(1, null), Scene(2, null)]));
        Assert.True(RVideoSceneLink.IsProjectLinked([Scene(1, null), Scene(2, RVideoSceneLinkModes.Frame)]));

        var panel = Read("TodoX.Web", "Components", "Pages", "RVideo", "RVideoSceneLinkPanel.razor");
        Assert.Contains("@if (LinkedVideo || _mode is not null)", panel);
        Assert.Contains("LinkedVideo=\"@RVideoSceneLink.IsProjectLinked(JobView.Project.Scenes)\"", Read("TodoX.Web", "Components", "Pages", "RVideo", "RVideoV2Preview.razor"));
    }

    [Fact]
    public void Auto_job_waits_for_start_only_until_a_scene_has_media()
    {
        var settings = new RVideoJobSettingsDto { ExecutionMode = RVideoExecutionModes.Auto };
        var bare = new[] { new VideoProjectSceneDto { SceneIndex = 1 }, new VideoProjectSceneDto { SceneIndex = 2 } };
        var withImage = new[] { new VideoProjectSceneDto { SceneIndex = 1, StaticImageUrl = "/a.png" }, new VideoProjectSceneDto { SceneIndex = 2 } };
        Assert.True(RVideoJobSettingsDto.AutoRunWaitingForStart(settings, bare));
        Assert.False(RVideoJobSettingsDto.AutoRunWaitingForStart(settings, withImage));
        Assert.False(RVideoJobSettingsDto.AutoRunWaitingForStart(new RVideoJobSettingsDto { ExecutionMode = RVideoExecutionModes.Manual }, bare));

        var worker = Read("TodoX.Web", "Services", "VideoRender", "RVideoLifecycleWorker.cs");
        Assert.Contains("project.Scenes.Any(RVideoJobSettingsDto.HasMedia)", worker);
        // The start button sits in step 2, next to "Tạo lại scene".
        var info = Read("TodoX.Web", "Components", "Pages", "RVideo", "RVideoV2Info.razor");
        Assert.True(info.IndexOf("Tạo lại scene", StringComparison.Ordinal) < info.IndexOf("rv2-start-auto-run", StringComparison.Ordinal));
        Assert.True(info.IndexOf("rv2-start-auto-run", StringComparison.Ordinal) < info.IndexOf("<b>Tạo ảnh</b>", StringComparison.Ordinal));
    }
}
