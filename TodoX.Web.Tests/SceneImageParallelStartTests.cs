using Microsoft.Extensions.Configuration;
using TodoX.Web.Services.Render;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>
/// Every scene of a job must start its image at the same time. The scene image child jobs used to share the
/// single <see cref="RenderJobWorker"/> lane, so an 8-scene job submitted its images one after another and the
/// preview page kept showing "Chưa có ảnh" for the scenes that had not been reached yet.
/// </summary>
public sealed class SceneImageParallelStartTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
            .Build();

    [Fact]
    public void Scene_images_run_on_as_many_lanes_as_a_job_has_scenes()
    {
        // A job of 8 scenes is the common case, so the default must carry all of them at once.
        Assert.Equal(8, SceneImageJobWorker.ResolveParallelism(Config()));
        Assert.Equal(4, SceneImageJobWorker.ResolveParallelism(Config(("RenderQueue:MaxConcurrentSceneImageJobs", "4"))));
        // Out-of-range values stay inside the safe band instead of flooding the provider or stopping the queue.
        Assert.Equal(16, SceneImageJobWorker.ResolveParallelism(Config(("RenderQueue:MaxConcurrentSceneImageJobs", "99"))));
        Assert.Equal(1, SceneImageJobWorker.ResolveParallelism(Config(("RenderQueue:MaxConcurrentSceneImageJobs", "0"))));
    }

    [Fact]
    public void The_image_queue_has_its_own_lanes_and_leaves_the_shared_lane()
    {
        var worker = Read("TodoX.Web", "Services", "Render", "SceneImageJobWorker.cs");
        Assert.Contains("ClaimNextByJobTypeAsync", worker);
        Assert.Contains("SceneImageRenderWorkItemHandler.JobTypeName", worker);
        // The lifecycle (billing, retries, terminal failures) stays exactly the shared one.
        Assert.Contains("RenderJobWorker.ProcessJobAsync", worker);

        var shared = Read("TodoX.Web", "Services", "Render", "RenderJobWorker.cs");
        Assert.Contains("RenderJobTypes.RenderSceneVideo, SceneImageRenderWorkItemHandler.JobTypeName", shared);

        Assert.Contains("AddHostedService<TodoX.Web.Services.Render.SceneImageJobWorker>()",
            Read("TodoX.Web", "Program.cs"));
        Assert.Contains("\"MaxConcurrentSceneImageJobs\"", Read("TodoX.Web", "appsettings.json"));
    }

    [Fact]
    public void The_preview_page_rereads_every_scene_image_state_while_polling()
    {
        var preview = Read("TodoX.Web", "Components", "Pages", "RVideo", "RVideoV2Preview.razor");
        // One query for the whole job, outside the "only the busy scenes" filter.
        Assert.Contains("SceneVersions.ListActiveImageSceneIdsAsync(ids)", preview);
        // The per-scene check is left only where a single "Tạo ảnh" click is guarded, never in the poll loop.
        Assert.Single(preview.Split("SceneVersions.HasActiveImageVersionAsync(")[1..]);
        Assert.Contains("if(await SceneVersions.HasActiveImageVersionAsync(scene.Id))", preview);
        // Polling must survive the gap between two steps of an AUTO run.
        Assert.Contains("|| AutoRunInProgress;", preview);
        Assert.Contains("view.Settings?.AutoRunActive == true", preview);

        var versions = Read("TodoX.Web", "Services", "VideoRender", "SceneMediaVersioningService.cs");
        Assert.Contains("ListActiveImageSceneIdsAsync", versions);
        Assert.Contains("scene_id = ANY(@sceneIds)", versions);
    }
}
