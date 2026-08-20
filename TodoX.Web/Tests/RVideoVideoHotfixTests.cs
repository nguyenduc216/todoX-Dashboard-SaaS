using System.Reflection;
using System.Text.Json;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoVideoHotfixTests
{
    [Fact]
    public void RVideoVideoPolicyIs79AiOnly()
    {
        Assert.All(RVideoVideoModelPolicy.Models, model =>
        {
            Assert.Equal(RVideoVideoModelPolicy.ProviderCode, model.ProviderCode);
            Assert.StartsWith("seedance_", model.Model, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal("seedance_20_pro", RVideoVideoModelPolicy.GetInitial().Model);
        Assert.True(RVideoVideoModelPolicy.Is79AiProvider("79ai"));
        Assert.True(RVideoVideoModelPolicy.Is79AiProvider("79ai_video"));
        Assert.False(RVideoVideoModelPolicy.Is79AiProvider("yescale_task_video"));
        Assert.Equal(4, RVideoVideoModelPolicy.Models.Count);
        Assert.Null(RVideoVideoModelPolicy.GetNext(3));
    }

    [Fact]
    public void BuildAttemptLogicalRequestIdKeepsAttemptZeroStable()
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("BuildAttemptLogicalRequestId", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        Assert.Equal("base", method!.Invoke(null, new object[] { "base", 0 }));
        Assert.Equal("base-fallback-2", method.Invoke(null, new object[] { "base", 2 }));
    }

    [Fact]
    public void ResolveNextAttemptIndexReusesActiveAttemptAndSkipsFailedOne()
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveNextAttemptIndex", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var active = new[]
        {
            new SceneVideoVersionDto { LogicalRequestId = "scene-base", Status = "submitted" }
        };
        var failed = new[]
        {
            new SceneVideoVersionDto { LogicalRequestId = "scene-base", Status = "failed" }
        };
        var fallback = new[]
        {
            new SceneVideoVersionDto { LogicalRequestId = "scene-base", Status = "failed" },
            new SceneVideoVersionDto { LogicalRequestId = "scene-base-fallback-1", Status = "failed" }
        };

        Assert.Equal(0, method!.Invoke(null, new object[] { "scene-base", active }));
        Assert.Equal(1, method.Invoke(null, new object[] { "scene-base", failed }));
        Assert.Equal(2, method.Invoke(null, new object[] { "scene-base", fallback }));
    }

    [Fact]
    public void BuildUsageMetadataCarriesAttemptLogicalRequestId()
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("BuildUsageMetadata", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var input = new SceneVideoRenderWorkItemInput
        {
            ParentJobId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ProjectId = 42,
            SceneId = 7,
            SceneIndex = 3,
            CustomerId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DurationSeconds = 12,
            AspectRatio = "9:16",
            Resolution = "720P",
            EstimatedUsd = 1.25m,
            CostSource = "configured_tariff",
            PricingMode = "fixed",
            PricingRuleKey = "rule-1"
        };

        var json = (string)method!.Invoke(null, new object[] { input, "scene-base-fallback-1", "task-123", "{\"ok\":true}", 9.5m })!;
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("scene-base-fallback-1", doc.RootElement.GetProperty("logicalRequestId").GetString());
        Assert.Equal("task-123", doc.RootElement.GetProperty("providerTaskId").GetString());
    }
}
