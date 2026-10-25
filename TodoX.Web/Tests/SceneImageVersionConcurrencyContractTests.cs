using Xunit;

namespace TodoX.Web.Tests;

public sealed class SceneImageVersionConcurrencyContractTests
{
    [Fact]
    public void LegacyImageJobIsDraftUntilTransactionalBindingAndActivation()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
        var page = File.ReadAllText(Path.Combine(root, "Components", "Pages", "RenderVideoJobs.razor"));
        var service = File.ReadAllText(Path.Combine(root, "Services", "VideoRender", "SceneMediaVersioningService.cs"));
        var start = page.IndexOf("var job = await RenderJobs.EnqueueAsync", StringComparison.Ordinal);
        var end = page.IndexOf("_sceneEventState[scene.Id] = RenderSceneImageVisualState.Queued", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var enqueue = page[start..end];
        Assert.Contains("InitialStatus = RenderJobStatuses.Draft", enqueue);
        Assert.Contains("TryActivateImageRenderJobAsync(version.Id, job.Id)", enqueue);
        var activation = service[service.IndexOf("public async Task<bool> TryActivateImageRenderJobAsync", StringComparison.Ordinal)..];
        Assert.True(activation.IndexOf("SET render_job_id=@renderJobId", StringComparison.Ordinal)
                    < activation.IndexOf("SET status='queued'", StringComparison.Ordinal));
        Assert.True(activation.IndexOf("SET status='queued'", StringComparison.Ordinal)
                    < activation.IndexOf("tx.Commit()", StringComparison.Ordinal));
    }

    [Fact]
    public void QueuedVersionChecksActiveSceneAfterIdempotentLookupAndBeforeInsert()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "Services", "VideoRender", "SceneMediaVersioningService.cs"));
        var begin = source.IndexOf("public async Task<SceneImageVersionDto> CreateQueuedImageVersionAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task CompleteImageVersionAsync", begin, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin);
        var method = source[begin..end];
        var lockIndex = method.IndexOf("await LockSceneAsync", StringComparison.Ordinal);
        var existingIndex = method.IndexOf("if (existing is not null)", StringComparison.Ordinal);
        var activeIndex = method.IndexOf("status IN ('queued','submitted','pending','processing','pending_reconciliation')", StringComparison.Ordinal);
        var insertIndex = method.IndexOf("INSERT INTO video_render.scene_image_versions", StringComparison.Ordinal);
        Assert.True(lockIndex >= 0 && lockIndex < existingIndex && existingIndex < activeIndex && activeIndex < insertIndex);
        Assert.Contains("throw new InvalidOperationException(\"SCENE_IMAGE_ACTIVE\")", method);
    }

    [Fact]
    public void V2ImageButtonsUseDraftActivationAndKeepUnknownOutcome()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
        var page = File.ReadAllText(Path.Combine(root, "Components", "Pages", "RVideo", "RVideoV2Preview.razor"));
        var action = File.ReadAllText(Path.Combine(root, "Services", "VideoRender", "RVideoSceneImageRenderAction.cs"));
        // Desktop under-image action, desktop overflow and mobile action share one handler.
        Assert.Equal(3, page.Split("OnClick=\"@(()=>HandleRerenderImageAsync(", StringSplitOptions.None).Length - 1);
        Assert.Contains("SceneImageRender.EnqueueRerenderAsync(", page);
        // The V2 preview keeps the separate video re-render action for videos; the image path must not
        // route through the video action. Assert that the image handler calls the image action, not the video one.
        var imageHandlerStart = page.IndexOf("async Task HandleRerenderImageAsync(", StringComparison.Ordinal);
        var imageHandlerEnd = page.IndexOf("public void Dispose()", imageHandlerStart, StringComparison.Ordinal);
        Assert.True(imageHandlerStart >= 0 && imageHandlerEnd > imageHandlerStart);
        var imageHandler = page[imageHandlerStart..imageHandlerEnd];
        Assert.Contains("SceneImageRender.EnqueueRerenderAsync(", imageHandler);
        Assert.DoesNotContain("SceneVideoRender.EnqueueRerenderAsync(", imageHandler);
        // TODOX-20261015-RVIDEO-IMAGE-ATOMIC-ENQUEUE-001: version + draft job + FK bind + draft->queued now
        // commit inside one service transaction, so the action must delegate instead of stitching two
        // separately committed steps (which could strand a version without a job).
        Assert.Contains("CreateQueuedImageVersionWithJobAsync(", action);
        Assert.DoesNotContain("RenderJobs.EnqueueAsync", action);
        Assert.DoesNotContain("MarkImageEnqueueUnknownAsync", action);
    }

    // TODOX-20261015-RVIDEO-IMAGE-ATOMIC-ENQUEUE-001: regression lock for the single-transaction enqueue.
    // A stranded version (committed) without its job must be unreachable: the version INSERT, job-draft INSERT,
    // FK bind and draft->queued activation must all happen before the first commit in that method.
    [Fact]
    public void AtomicImageEnqueueCommitsVersionJobBindAndActivationOnce()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "Services", "VideoRender", "SceneMediaVersioningService.cs"));
        var begin = source.IndexOf("public async Task<SceneImageEnqueueOutcome> CreateQueuedImageVersionWithJobAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task CompleteImageVersionAsync", begin, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin);
        var method = source[begin..end];

        var insertVersion = method.IndexOf("INSERT INTO video_render.scene_image_versions", StringComparison.Ordinal);
        var insertJob = method.IndexOf("INSERT INTO render.render_jobs", StringComparison.Ordinal);
        var bind = method.IndexOf("SET render_job_id=@jobId", StringComparison.Ordinal);
        var activate = method.IndexOf("UPDATE render.render_jobs SET status='queued'", StringComparison.Ordinal);
        // The early idempotent-lookup branch returns before the create path; its own commit must not be
        // counted, so locate the commit that follows activation.
        var commit = method.IndexOf("tx.Commit()", activate, StringComparison.Ordinal);

        Assert.True(insertVersion >= 0, "version insert must exist");
        Assert.True(insertJob > insertVersion, "job insert must follow the version insert");
        Assert.True(bind > insertJob, "FK bind must follow the job insert");
        Assert.True(activate > bind, "draft->queued activation must follow the FK bind");
        Assert.True(commit > activate, "the single commit must come after activation");
        // Exactly one commit on the create path: from the version insert to the end of the method.
        Assert.Equal(1, method[insertVersion..].Split("tx.Commit()", StringSplitOptions.None).Length - 1);
    }
}
