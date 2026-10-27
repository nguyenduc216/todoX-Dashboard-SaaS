using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoPostgresFactAttribute : FactAttribute
{
    public RVideoPostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TODOX_RVIDEO_PG_APPROVED") != "YES")
            Skip = "Requires approved disposable PostgreSQL project, stopped workers and local scene fixtures; see rollout runbook.";
    }
}

public sealed class RVideoRecomposePostgresTests
{
    private sealed class Pointer
    {
        public Guid? Selected { get; set; }
        public string? Url { get; set; }
        public string? Path { get; set; }
        public string Status { get; set; } = "";
        public string? Error { get; set; }
    }
    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException("Missing approved test environment variable: " + name);

    [RVideoPostgresFact]
    public async Task RealTransactionsSerializeRecomposeAndPreserveLegacyFinalUntilValidatedPublication()
    {
        if (Required("TODOX_RVIDEO_WORKERS_STOPPED") != "YES") throw new InvalidOperationException("Stop all staging workers before this test.");
        var settings = new NpgsqlConnectionStringBuilder(Required("TODOX_RVIDEO_PG_CONNECTION"));
        var expectedDatabase = Required("TODOX_RVIDEO_PG_DATABASE");
        if (!expectedDatabase.Contains("test", StringComparison.OrdinalIgnoreCase)
            || expectedDatabase.Contains("prod", StringComparison.OrdinalIgnoreCase)
            || settings.Database != expectedDatabase || settings.Host != Required("TODOX_RVIDEO_PG_HOST")
            || settings.Username != Required("TODOX_RVIDEO_PG_USER") || settings.Port != int.Parse(Required("TODOX_RVIDEO_PG_PORT"))
            || settings.Host.Contains(',') || settings.Host.Contains("prod", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Isolated PostgreSQL database allowlist mismatch.");
        var projectId = long.Parse(Required("TODOX_RVIDEO_PROJECT"));
        var tenantId = Guid.Parse(Required("TODOX_RVIDEO_TENANT"));
        var owner = Guid.Parse(Required("TODOX_RVIDEO_OWNER"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:TodoXSaaS"] = settings.ConnectionString, ["TodoX:TenantId"] = tenantId.ToString()
        }).Build();
        var factory = new TodoXConnectionFactory(config);
        var tenant = new TenantContext(factory, config);
        await tenant.EnsureLoadedAsync();
        using var provider = new ServiceCollection().AddSingleton<IConfiguration>(config).AddSingleton(factory).AddSingleton(tenant).BuildServiceProvider();
        var versions = ActivatorUtilities.CreateInstance<SceneMediaVersioningService>(provider);
        Assert.True((await versions.GetFinalVideoVersioningStateAsync()).Ready, "Preflight/schema/flag must already be approved and ready; this test never enables flags or creates schema.");
        var jobs = new RenderJobService(factory, tenant, config, NullLogger<RenderJobService>.Instance);
        using var db = await factory.OpenAsync();
        const string pointerSql = "SELECT selected_final_video_version_id AS Selected,final_video_url AS Url,final_video_path AS Path,status AS Status,error_message AS Error FROM video_render.video_projects WHERE id=@projectId AND tenant_id=@tenantId AND user_id=@owner;";
        var original = await db.QuerySingleAsync<Pointer>(pointerSql, new { projectId, tenantId, owner });
        Assert.Null(original.Selected); // Dedicated legacy Completed fixture, not an existing versioned production project.
        Assert.Equal(VideoProjectStatuses.Completed, original.Status);
        Assert.False(string.IsNullOrWhiteSpace(original.Url));
        var originalJobs = (await jobs.ListByLogCodeAsync(RVideoProjectFinalizationService.BuildMergeLogicalRequestKey(projectId))).ToArray();
        Assert.DoesNotContain(originalJobs, j => FinalMergeMessages.IsActive(j.Status));
        var originalScenes = (await db.QueryAsync<Guid>("SELECT selected_video_version_id FROM video_render.video_project_scenes WHERE project_id=@projectId AND tenant_id=@tenantId ORDER BY scene_index;", new { projectId, tenantId })).ToArray();
        var createdJobs = new HashSet<Guid>();
        var createdVersions = new HashSet<Guid>();
        var outputRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "todox-pg-final-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputRoot);
        async Task<VideoProjectDto?> GetProject(long id, CancellationToken ct)
        {
            using var connection = await factory.OpenAsync(ct);
            var p = await connection.QuerySingleAsync<VideoProjectDto>("SELECT id AS Id,user_id AS UserId,customer_id AS CustomerId,status AS Status,final_video_url AS FinalVideoUrl,final_video_path AS FinalVideoPath FROM video_render.video_projects WHERE id=@id AND tenant_id=@tenantId AND user_id=@owner;", new { id, tenantId, owner });
            p.Scenes = (await connection.QueryAsync<VideoProjectSceneDto>("SELECT id AS Id,scene_index AS SceneIndex,duration_seconds AS DurationSeconds FROM video_render.video_project_scenes WHERE project_id=@id AND tenant_id=@tenantId ORDER BY scene_index;", new { id, tenantId })).ToList();
            return p;
        }
        RVideoProjectFinalizationService Service() => new(GetProject,
            (_, _) => Task.FromResult<RVideoJobSettingsDto?>(new() { VoiceMode = RVideoVoiceModes.Native }),
            (_, _, _, _, _, _) => Task.CompletedTask, versions, jobs, (_, _, _, _) => Task.CompletedTask,
            () => new() { UserId = owner, IsAuthenticated = true });
        try
        {
            var results = await Task.WhenAll(Service().TryEnqueueFinalMergeAsync(projectId, RVideoProjectFinalizationContracts.TriggerManual), Service().TryEnqueueFinalMergeAsync(projectId, RVideoProjectFinalizationContracts.TriggerManual));
            foreach (var result in results) if (result.RenderJobId is Guid id) createdJobs.Add(id);
            Assert.Single(results, r => r.Enqueued);
            Assert.Single(results, r => r.AlreadyActive);
            Assert.Single(createdJobs);
            var firstJob = createdJobs.Single();
            var p = (await GetProject(projectId, default))!;
            async Task<FinalVideoVersionDto> Queue(Guid jobId)
            {
                var version = await versions.CreateQueuedFinalVideoVersionAsync(new(projectId, owner, p.CustomerId, jobId,
                    "integration-final-" + jobId, new { previousFinal = original }, new { }, new { }, new { }));
                createdVersions.Add(version.Id);
                return version;
            }
            var failed = await Queue(firstJob);
            // Repeating the same identity must return the same queued version/items, without backfilling the legacy final.
            Assert.Equal(failed.Id, (await Queue(firstJob)).Id);
            Assert.Equal(originalScenes, (await versions.ListFinalVideoVersionItemsAsync(failed.Id)).OrderBy(x => x.ItemOrder).Select(x => x.SceneVideoVersionId));
            await versions.FailFinalVideoVersionAsync(failed.Id, "TEST_FAILURE", "Injected isolated-test failure");
            await jobs.MarkStatusAsync(firstJob, RenderJobStatuses.Failed);
            var afterFailure = await db.QuerySingleAsync<Pointer>(pointerSql, new { projectId, tenantId, owner });
            Assert.Equal(original.Selected, afterFailure.Selected);
            Assert.Equal(original.Url, afterFailure.Url);
            Assert.Equal(original.Path, afterFailure.Path);
            var retry = await Service().TryEnqueueFinalMergeAsync(projectId, RVideoProjectFinalizationContracts.TriggerManual);
            Assert.True(retry.Enqueued);
            createdJobs.Add(retry.RenderJobId!.Value);
            Assert.NotEqual((await jobs.GetAsync(firstJob))!.InputJson, (await jobs.GetAsync(retry.RenderJobId.Value))!.InputJson);
            var successful = await Queue(retry.RenderJobId.Value);
            var inputs = new List<FinalMergeMedia.Input>();
            var allowedRoot = System.IO.Path.GetFullPath(Required("TODOX_RVIDEO_MEDIA_ROOT")).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            foreach (var scene in p.Scenes)
            {
                var video = (await versions.GetSelectedVideoVersionAsync(scene.Id))!;
                var file = System.IO.Path.GetFullPath(video.SourceFilePath!);
                Assert.StartsWith(allowedRoot, file, StringComparison.OrdinalIgnoreCase);
                Assert.True(File.Exists(file));
                inputs.Add(new(file, scene.DurationSeconds));
            }
            var concat = System.IO.Path.Combine(outputRoot, "concat.txt");
            await File.WriteAllLinesAsync(concat, VideoRenderMergeHandler.BuildTimedConcatLines(inputs.Select(i => (i.Path, i.Seconds))), new System.Text.UTF8Encoding(false));
            var output = System.IO.Path.Combine(outputRoot, "validated.mp4");
            var duration = inputs.Sum(i => i.Seconds);
            await FinalMergeMedia.MergeAsync(Required("TODOX_TEST_FFMPEG"), outputRoot, concat, output, inputs, duration, TimeSpan.FromMinutes(3), _ => Task.CompletedTask, default);
            Assert.Null(await FinalMergeMedia.ValidateAsync(Required("TODOX_TEST_FFMPEG"), output, duration, TimeSpan.FromMinutes(3), default));
            // Real transaction: publish only after genuine local FFmpeg validation; no fabricated completed version.
            await versions.CompleteFinalVideoVersionAsync(successful.Id, new(new Uri(output).AbsoluteUri, output, null, duration, "video/mp4"));
            using var readTransaction = db.BeginTransaction(System.Data.IsolationLevel.RepeatableRead);
            var selected = await db.QuerySingleAsync<Pointer>(pointerSql, new { projectId, tenantId, owner }, readTransaction);
            Assert.Equal(successful.Id, selected.Selected);
            Assert.Equal(output, selected.Path);
            Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT count(*) FROM video_render.final_video_versions WHERE project_id=@projectId AND tenant_id=@tenantId AND is_selected AND status='completed' AND id=@id AND public_url=@url AND source_file_path=@path;", new { projectId, tenantId, id = selected.Selected, url = selected.Url, path = selected.Path }, readTransaction));
            readTransaction.Commit();
            Assert.Equal(originalScenes, (await db.QueryAsync<Guid>("SELECT selected_video_version_id FROM video_render.video_project_scenes WHERE project_id=@projectId AND tenant_id=@tenantId ORDER BY scene_index;", new { projectId, tenantId })).ToArray());
            foreach (var id in createdJobs)
            {
                var job = (await jobs.GetAsync(id))!;
                Assert.Equal(RenderJobTypes.MergeProjectVideo, job.JobType);
                Assert.Equal(0, job.PointCostEstimate);
                Assert.Equal(0, job.PointCostCharged);
                Assert.True(string.IsNullOrWhiteSpace(job.ProviderTaskId));
            }
            Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT count(*) FROM public.todox_ai_provider_usage_log WHERE job_id=ANY(@ids) OR request_id=ANY(@ids);", new { ids = createdJobs.Select(i => i.ToString()).ToArray() }));
        }
        finally
        {
            // Recover committed enqueue IDs even if writing its job event failed before a response.
            foreach (var row in await jobs.ListByLogCodeAsync(RVideoProjectFinalizationService.BuildMergeLogicalRequestKey(projectId)))
                if (originalJobs.All(previous => previous.Id != row.Id)) createdJobs.Add(row.Id);
            // Only approved disposable fixture pointers and rows created by this test are restored/removed.
            using var cleanup = db.BeginTransaction();
            await db.ExecuteAsync("UPDATE video_render.video_projects SET selected_final_video_version_id=@Selected,final_video_url=@Url,final_video_path=@Path,status=@Status,error_message=@Error WHERE id=@projectId AND tenant_id=@tenantId;", new { original.Selected, original.Url, original.Path, original.Status, original.Error, projectId, tenantId }, cleanup);
            foreach (var id in createdVersions)
            {
                await db.ExecuteAsync("DELETE FROM video_render.final_video_version_items WHERE final_video_version_id=@id; DELETE FROM video_render.final_video_versions WHERE id=@id AND tenant_id=@tenantId;", new { id, tenantId }, cleanup);
            }
            foreach (var id in createdJobs)
                await db.ExecuteAsync("DELETE FROM render.render_job_events WHERE job_id=@id AND tenant_id=@tenantId; DELETE FROM render.render_jobs WHERE id=@id AND tenant_id=@tenantId;", new { id, tenantId }, cleanup);
            cleanup.Commit();
            // Retain generated local media/log evidence for review; never touch the selected scene source files.
        }
    }
}
