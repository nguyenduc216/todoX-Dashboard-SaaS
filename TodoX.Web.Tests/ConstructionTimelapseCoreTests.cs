using System.Text.Json;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services.Platform;
using TodoX.Web.Services.Timelapse;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class ConstructionTimelapseCoreTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Adapter_UsesCanonicalConstructionServiceCode()
    {
        var adapter = new ConstructionTimelapseAdapter(new CapturingExecutionBridge());

        Assert.Equal("CONSTRUCTION_VIDEO", adapter.ServiceCode);
    }

    [Fact]
    public async Task Router_ResolvesConstructionVideoAndAdapterReturnsDeferredCorrelation()
    {
        var legacyJobId = Guid.NewGuid();
        var bridge = new CapturingExecutionBridge(legacyJobId);
        var adapter = new ConstructionTimelapseAdapter(bridge);
        var router = new CoreExecutionRouter(new ICoreJobExecutionAdapter[] { adapter });
        var context = CreateContext();

        Assert.True(router.CanHandle("CONSTRUCTION_VIDEO"));

        var result = await router.DispatchAsync(context);

        Assert.Equal(CoreExecutionDisposition.Deferred, result.Disposition);
        Assert.Equal("todox", result.ExecutionSystem);
        Assert.Equal(legacyJobId.ToString(), result.ExternalExecutionId);
        Assert.Equal("construction_timelapse", result.Adapter);
        Assert.Equal(context.CoreJobId, bridge.LastContext?.CoreJobId);
        Assert.NotNull(result.Metadata);
        Assert.Equal(
            context.CoreJobId,
            result.Metadata!.Value.GetProperty("core_job_id").GetGuid());
        Assert.Equal(
            legacyJobId,
            result.Metadata.Value.GetProperty("legacy_job_id").GetGuid());
    }

    [Fact]
    public void InputMapping_ReusesLegacyTimelapseFieldsAndNormalizesAliases()
    {
        var mediaId = Guid.NewGuid();
        var context = CreateContext(JsonSerializer.SerializeToElement(new
        {
            profile_code = "townhouse",
            scene_count = 4,
            quality_tier = "premium",
            aspect_ratio = "9:16",
            duration_seconds = 6,
            title = "Construction launch"
        }), JsonSerializer.SerializeToElement(new[]
        {
            new { role = "original_image", media_id = mediaId }
        }));

        var mapped = ConstructionTimelapseExecutionBridge.MapRequest(context);

        Assert.Equal("townhouse", mapped.ProfileCode);
        Assert.Equal(4, mapped.SceneCount);
        Assert.Equal(TimelapseRequestRules.ProfessionalMode, mapped.VideoMode);
        Assert.Equal(TimelapseRequestRules.PortraitRatio, mapped.Ratio);
        Assert.Equal("Construction launch", mapped.Title);
        Assert.Equal(mediaId, mapped.OriginalImageMediaId);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void InputMapping_AcceptsExistingSceneCounts(int sceneCount)
    {
        var mapped = ConstructionTimelapseExecutionBridge.MapRequest(
            CreateContext(JsonSerializer.SerializeToElement(new
            {
                profileCode = "construction",
                sceneCount,
                videoMode = "fast",
                ratio = "16_9",
                originalImageMediaId = Guid.NewGuid()
            })));

        Assert.Equal(sceneCount, mapped.SceneCount);
    }

    [Fact]
    public void InputMapping_AcceptsCaseInsensitiveOriginalImageReferenceRole()
    {
        var mediaId = Guid.NewGuid();
        var context = CreateContext(
            JsonSerializer.SerializeToElement(new
            {
                profileCode = "construction",
                sceneCount = 3,
                videoMode = "fast",
                ratio = "16_9"
            }),
            JsonSerializer.SerializeToElement(new[]
            {
                new { role = "ORIGINAL_IMAGE", mediaId }
            }));

        var mapped = ConstructionTimelapseExecutionBridge.MapRequest(context);

        Assert.Equal(mediaId, mapped.OriginalImageMediaId);
    }

    [Fact]
    public void InputMapping_AcceptsCanonicalSourceImageField()
    {
        var mediaId = Guid.NewGuid();
        var context = CreateContext(JsonSerializer.SerializeToElement(new
        {
            profileCode = "construction",
            sceneCount = 3,
            videoMode = "fast",
            ratio = "16_9",
            source_image = mediaId
        }));

        var mapped = ConstructionTimelapseExecutionBridge.MapRequest(context);

        Assert.Equal(mediaId, mapped.OriginalImageMediaId);
    }

    [Fact]
    public void InputMapping_RejectsProviderOptionsAndChangedDuration()
    {
        var context = CreateContext(JsonSerializer.SerializeToElement(new
        {
            profileCode = "construction",
            sceneCount = 3,
            videoMode = "seedance_provider_mode",
            ratio = "16_9",
            durationSeconds = 8,
            originalImageMediaId = Guid.NewGuid()
        }));

        Assert.Throws<InvalidOperationException>(
            () => ConstructionTimelapseExecutionBridge.MapRequest(context));
    }

    [Theory]
    [InlineData(null, CoreFailureBillingPolicy.ReleaseReservation)]
    [InlineData("", CoreFailureBillingPolicy.ReleaseReservation)]
    [InlineData("provider-task-1", CoreFailureBillingPolicy.KeepCharge)]
    public void FailurePolicy_IsConservativeAroundProviderConsumption(
        string? providerTaskId,
        CoreFailureBillingPolicy expected)
    {
        Assert.Equal(expected, TimelapseProviderRuntime.FailurePolicy(providerTaskId));
    }

    [Theory]
    [InlineData(CoreFailureBillingPolicy.ReleaseReservation, false, CoreFailureBillingPolicy.ReleaseReservation)]
    [InlineData(CoreFailureBillingPolicy.ReleaseReservation, true, CoreFailureBillingPolicy.KeepCharge)]
    [InlineData(CoreFailureBillingPolicy.KeepCharge, false, CoreFailureBillingPolicy.KeepCharge)]
    public void TerminalFailurePolicy_PreservesChargeAfterProviderTaskStarts(
        CoreFailureBillingPolicy requested,
        bool providerTaskStarted,
        CoreFailureBillingPolicy expected)
    {
        Assert.Equal(
            expected,
            TimelapseCoreLifecycleBridge.ResolveFailurePolicy(requested, providerTaskStarted));
    }

    [Fact]
    public void CompletionOutput_IsTransportNeutralAndCorrelated()
    {
        var legacyJobId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();

        var output = TimelapseCoreLifecycleBridge.BuildOutput(
            legacyJobId,
            mediaId,
            "timelapse/final.mp4",
            "/uploads/timelapse/final.mp4");
        var item = Assert.Single(output.GetProperty("outputs").EnumerateArray());

        Assert.Equal("video", item.GetProperty("type").GetString());
        Assert.Equal("video/mp4", item.GetProperty("mime_type").GetString());
        Assert.Equal("/uploads/timelapse/final.mp4", item.GetProperty("url").GetString());
        Assert.Equal(
            legacyJobId,
            item.GetProperty("metadata").GetProperty("legacy_job_id").GetGuid());
        Assert.Equal(
            "CONSTRUCTION_VIDEO",
            item.GetProperty("metadata").GetProperty("service_code").GetString());
    }

    [Fact]
    public void SourceContracts_PreserveLegacyPathAndBridgeCoreLifecycle()
    {
        var adapter = ReadSource(
            "TodoX.Web",
            "Services",
            "Timelapse",
            "ConstructionTimelapseAdapter.cs");
        var lifecycle = ReadSource(
            "TodoX.Web",
            "Services",
            "Timelapse",
            "TimelapseCoreLifecycleBridge.cs");
        var provider = ReadSource(
            "TodoX.Web",
            "Services",
            "Timelapse",
            "TimelapseProviderRuntime.cs");
        var finalizer = ReadSource(
            "TodoX.Web",
            "Services",
            "Timelapse",
            "TimelapseFinalizerRuntime.cs");
        var program = ReadSource("TodoX.Web", "Program.cs");
        var api = ReadSource(
            "TodoX.Web",
            "Services",
            "Platform",
            "CoreApiEndpointExtensions.cs");
        var coreBilling = ReadSource(
            "TodoX.Web",
            "Services",
            "Platform",
            "CoreBillingService.cs");

        Assert.Contains("input_json->>'coreJobId'=@coreJobId", adapter, StringComparison.Ordinal);
        Assert.Contains("pointStatus = RenderPointStatuses.NotRequired", adapter, StringComparison.Ordinal);
        Assert.Contains("_workflow.StartOrResumeAsync(", adapter, StringComparison.Ordinal);
        Assert.Contains("CoreExecutionResult.Deferred(", adapter, StringComparison.Ordinal);
        Assert.Contains("pg_advisory_xact_lock", adapter, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_CORE_BRIDGE_CREATED", adapter, StringComparison.Ordinal);

        Assert.Contains("_completion.MarkProgressAsync(", lifecycle, StringComparison.Ordinal);
        Assert.Contains("current.ProgressPercent >= progressPercent", lifecycle, StringComparison.Ordinal);
        Assert.Contains("_workflow.StartFinalizerAsync(", lifecycle, StringComparison.Ordinal);
        Assert.Contains("_completion.CompleteAsync(", lifecycle, StringComparison.Ordinal);
        Assert.Contains("_completion.FailAsync(", lifecycle, StringComparison.Ordinal);
        Assert.Contains("TimelapseParentStatuses.Failed", lifecycle, StringComparison.Ordinal);
        Assert.Contains("ProviderTaskStarted", lifecycle, StringComparison.Ordinal);
        Assert.Contains("ReconcileCompletionAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("legacy.status='FAILED'", lifecycle, StringComparison.Ordinal);
        Assert.Contains("FinalizerFailed", lifecycle, StringComparison.Ordinal);

        Assert.Contains("CoreFailureBillingPolicy.ReleaseReservation", provider, StringComparison.Ordinal);
        Assert.Contains("CoreFailureBillingPolicy.KeepCharge", provider, StringComparison.Ordinal);
        Assert.Contains("_coreLifecycle.AdvanceAsync(", provider, StringComparison.Ordinal);
        Assert.Contains("_coreLifecycle.CompleteAsync(", finalizer, StringComparison.Ordinal);
        Assert.Contains("CoreFailureBillingPolicy.KeepCharge", finalizer, StringComparison.Ordinal);

        Assert.Contains(
            "AddScoped<ICoreJobExecutionAdapter, ConstructionTimelapseAdapter>",
            program,
            StringComparison.Ordinal);
        Assert.DoesNotContain("/complete", api, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/fail", api, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "if (job.Status == RenderJobStatuses.Completed)",
            coreBilling,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE TABLE", adapter, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE", lifecycle, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExistingTimelapseSceneRatiosRemainUnchanged()
    {
        Assert.Equal(new[] { 0, 35, 70, 100 }, TimelapseRequestRules.GetProgressMapping(3));
        Assert.Equal(new[] { 0, 25, 50, 75, 100 }, TimelapseRequestRules.GetProgressMapping(4));
        Assert.Equal(new[] { 0, 20, 40, 60, 80, 100 }, TimelapseRequestRules.GetProgressMapping(5));
        Assert.Equal(new[] { 0, 25, 40, 55, 70, 85, 100 }, TimelapseRequestRules.GetProgressMapping(6));
    }

    private static CoreJobDispatchContext CreateContext(
        JsonElement? input = null,
        JsonElement? references = null)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ConstructionTimelapseAdapter.ConstructionServiceCode,
            new CoreRequestContext(
                Guid.NewGuid(),
                Guid.NewGuid(),
                CoreChannelCodes.Api,
                "test-client",
                "test-request"),
            input ?? JsonSerializer.SerializeToElement(new
            {
                profileCode = "construction",
                sceneCount = 3,
                videoMode = "fast",
                ratio = "16_9",
                originalImageMediaId = Guid.NewGuid()
            }),
            null,
            references);

    private static string ReadSource(params string[] path)
        => File.ReadAllText(Path.Combine([RepositoryRoot, .. path]));

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

    private sealed class CapturingExecutionBridge : IConstructionTimelapseExecutionBridge
    {
        private readonly Guid _legacyJobId;

        public CapturingExecutionBridge(Guid? legacyJobId = null)
        {
            _legacyJobId = legacyJobId ?? Guid.NewGuid();
        }

        public CoreJobDispatchContext? LastContext { get; private set; }

        public Task<ConstructionTimelapseExecution> StartAsync(
            CoreJobDispatchContext context,
            CancellationToken ct = default)
        {
            LastContext = context;
            return Task.FromResult(new ConstructionTimelapseExecution(
                _legacyJobId,
                new TimelapseJobSnapshot
                {
                    CoreJobId = context.CoreJobId,
                    ServiceId = context.ServiceId,
                    ServiceCode = context.ServiceCode
                }));
        }
    }
}
