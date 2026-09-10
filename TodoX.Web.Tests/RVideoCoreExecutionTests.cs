using System.Text.Json;
using TodoX.Web.Services.Platform;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoCoreExecutionTests
{
    [Fact]
    public async Task CoreServiceHandler_AdaptsLegacyRVideoSnapshotAndDispatchesIt()
    {
        var serviceId = Guid.NewGuid();
        var coreJobId = Guid.NewGuid();
        var adapter = new CapturingAdapter();
        var completion = new CapturingCompletion();
        var handler = new CoreServiceJobHandler(
            new CoreExecutionRouter(new ICoreJobExecutionAdapter[] { adapter }),
            completion);
        var input = JsonSerializer.Serialize(new
        {
            engine = "RVIDEO",
            serviceId,
            serviceCode = "RVIDEO",
            projectId = 64,
            sceneIds = new[] { 334, 335, 336, 337 },
            prompt = new { title = "Ngày mai con đi tiêm" }
        });

        await Assert.ThrowsAsync<RenderJobDeferredException>(() =>
            handler.HandleAsync(new RenderJobDto
            {
                Id = coreJobId,
                JobType = RenderJobTypes.CoreService,
                InputJson = input
            }, CancellationToken.None));

        Assert.Equal(coreJobId, adapter.Context?.CoreJobId);
        Assert.Equal(serviceId, adapter.Context?.ServiceId);
        Assert.Equal("RVIDEO", adapter.Context?.ServiceCode);
        Assert.Equal(64, adapter.Context?.Input.GetProperty("projectId").GetInt64());
        Assert.Equal(4, adapter.Context?.Input.GetProperty("sceneIds").GetArrayLength());
        Assert.Equal(1, completion.ProgressCalls);
        Assert.NotNull(completion.Deferred);
    }

    [Fact]
    public void RVideoCoreAdapter_ParsesProjectAndSceneIdsFromCoreInput()
    {
        var request = RVideoCoreExecutionAdapter.ParseRequest(JsonSerializer.SerializeToElement(new
        {
            projectId = 64,
            sceneIds = new[] { 334, 335, 336, 337 },
            aspectRatio = "9:16",
            resolution = "720p"
        }));

        Assert.Equal(64, request.ProjectId);
        Assert.Equal(new long[] { 334, 335, 336, 337 }, request.SceneIds);
        Assert.Equal("9:16", request.AspectRatio);
        Assert.Equal("720p", request.Resolution);
    }

    [Fact]
    public void RVideoCoreAdapter_ParsesProjectAndSceneIdsFromNestedPrompt()
    {
        var request = RVideoCoreExecutionAdapter.ParseRequest(JsonSerializer.SerializeToElement(new
        {
            engine = "RVIDEO",
            prompt = new
            {
                projectId = 64,
                sceneIds = new[] { 334, 335, 336, 337 }
            }
        }));

        Assert.Equal(64, request.ProjectId);
        Assert.Equal(new long[] { 334, 335, 336, 337 }, request.SceneIds);
    }

    [Fact]
    public void RVideoCoreAdapter_ParsesProjectAndSceneIdsFromJsonPromptString()
    {
        var request = RVideoCoreExecutionAdapter.ParseRequest(JsonSerializer.SerializeToElement(new
        {
            engine = "RVIDEO",
            prompt = JsonSerializer.Serialize(new
            {
                projectId = 64,
                sceneIds = new[] { 334, 335, 336, 337 },
                aspectRatio = "16:9",
                resolution = "1080p"
            })
        }));

        Assert.Equal(64, request.ProjectId);
        Assert.Equal(new long[] { 334, 335, 336, 337 }, request.SceneIds);
        Assert.Equal("16:9", request.AspectRatio);
        Assert.Equal("1080p", request.Resolution);
    }

    [Fact]
    public void RVideoCoreDispatchContractsAreRegisteredAndObservable()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "TodoX.Web", "Program.cs"));
        var adapter = File.ReadAllText(Path.Combine(root, "TodoX.Web", "Services", "VideoRender", "RVideoCoreExecutionAdapter.cs"));
        var batch = File.ReadAllText(Path.Combine(root, "TodoX.Web", "Services", "VideoRender", "SceneVideoRenderHandler.cs"));
        var worker = File.ReadAllText(Path.Combine(root, "TodoX.Web", "Services", "Render", "RenderJobService.cs"));
        var completion = File.ReadAllText(Path.Combine(root, "TodoX.Web", "Services", "Platform", "CoreJobCompletionService.cs"));

        Assert.Contains("AddScoped<ICoreJobExecutionAdapter, TodoX.Web.Services.VideoRender.RVideoCoreExecutionAdapter>()", program);
        Assert.Contains("CORE_EXECUTION_STARTED", adapter);
        Assert.Contains("CORE_RVIDEO_DISPATCH_STARTED", adapter);
        Assert.Contains("CORE_RVIDEO_BATCH_CREATED", adapter);
        Assert.Contains("CORE_RVIDEO_SCENE_JOBS_CREATED", batch);
        Assert.Contains("job_type='core_service'", worker);
        Assert.Contains("AND attempt_count=0", worker);
        Assert.Contains("EnqueueForProjectIfNoneActiveAsync", adapter);
        Assert.Contains("AlreadyCompleted", batch);
        Assert.Contains("progress = 1", completion);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TodoX.Dashboard.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find TodoX repository root.");
    }

    private sealed class CapturingAdapter : ICoreJobExecutionAdapter
    {
        public string ServiceCode => "RVIDEO";
        public CoreJobDispatchContext? Context { get; private set; }

        public Task<CoreExecutionResult> DispatchAsync(CoreJobDispatchContext context, CancellationToken ct = default)
        {
            Context = context;
            return Task.FromResult(CoreExecutionResult.Deferred("render", "batch-job", "test-rvideo"));
        }
    }

    private sealed class CapturingCompletion : ICoreJobCompletionService
    {
        public int ProgressCalls { get; private set; }
        public CoreExecutionCorrelation? Deferred { get; private set; }

        public Task MarkDeferredAsync(CoreExecutionAuthority authority, Guid jobId, CoreExecutionCorrelation correlation, string? message = null, CancellationToken ct = default)
        {
            Deferred = correlation;
            return Task.CompletedTask;
        }

        public Task MarkProgressAsync(CoreExecutionAuthority authority, CoreJobProgressRequest request, CancellationToken ct = default)
        {
            ProgressCalls++;
            return Task.CompletedTask;
        }

        public Task<CoreBillingCompletion> CompleteAsync(CoreExecutionAuthority authority, CoreJobCompleteRequest request, CancellationToken ct = default)
            => Task.FromResult(new CoreBillingCompletion(true, RenderPointStatuses.Charged, 0, null));

        public Task<CoreBillingCompletion> FailAsync(CoreExecutionAuthority authority, CoreJobFailRequest request, CancellationToken ct = default)
            => Task.FromResult(new CoreBillingCompletion(true, RenderPointStatuses.Cancelled, 0, null));
    }
}
