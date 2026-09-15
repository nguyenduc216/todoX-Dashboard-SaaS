using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.DanceSell;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class BillingAndRatioRegressionTests
{
    [Fact]
    public void InsufficientPointsFormatterIncludesRequiredAvailableAndMissing()
    {
        var message = AiImageBillingMessageFormatter.FormatInsufficientPoints(173m, 102m, "tạo video");

        Assert.Contains("173", message);
        Assert.Contains("102", message);
        Assert.Contains("71", message);
        Assert.Contains("Không đủ điểm để tạo video", message);
    }

    [Fact]
    public void RequestedRatioOverridesProviderRouteDefaults()
    {
        var route = new DanceSellProviderRouteDto
        {
            ConfigJson = "{\"ratio\":\"4:5\",\"provider_ratio\":\"1:1\"}"
        };

        Assert.Equal("16:9", DanceSellMotionProviderContract.ResolveProviderRatio(route, "16:9"));
        Assert.Equal("4:5", DanceSellMotionProviderContract.ResolveProviderRatio(route, null));
    }

    [Fact]
    public void TimelapseVideoStartNoLongerIncludesAutoFinishBypass()
    {
        var workflow = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "TodoX.Web",
            "Services",
            "Timelapse",
            "TimelapseWorkflowService.cs"));

        Assert.DoesNotContain("COALESCE((j.input_json->>'autoFinish')::boolean, false)=true", workflow);
        Assert.Contains("videoRenderConfirmed", workflow);
        Assert.Contains("requireVideoConfirmation", workflow);
    }
}
