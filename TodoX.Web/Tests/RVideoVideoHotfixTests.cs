using System.Reflection;
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
}
