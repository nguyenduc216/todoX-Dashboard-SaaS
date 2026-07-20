using System.Text.Json;
using TodoX.Web.Services.AiProviders;
using Xunit;

namespace TodoX.Web.Tests;

public class YEScaleVideoModelMapperTests
{
    [Fact]
    public void Mapper_BuildsGrokVideoPayload()
    {
        var payload = YEScaleVideoModelMapper.BuildSubmitRequest(
            "grok-video",
            "camera dolly",
            "https://cdn.example/frame.png",
            "16:9",
            "1080P",
            10,
            providerConfigJson: null,
            capabilityConfigJson: null);

        var json = JsonSerializer.Serialize(payload);
        Assert.Contains("\"model\":\"grok-video\"", json);
        Assert.Contains("\"images\":[\"https://cdn.example/frame.png\"]", json);
        Assert.Contains("\"aspect_ratio\":\"16:9\"", json);
        Assert.Contains("\"duration\":10", json);
        Assert.Contains("\"size\":\"1080P\"", json);
    }

    [Fact]
    public void Mapper_BuildsGrokVideo15Payload()
    {
        var payload = YEScaleVideoModelMapper.BuildSubmitRequest(
            "grok-video-1.5",
            "hero motion",
            "https://cdn.example/frame.png",
            "9:16",
            "720P",
            6,
            providerConfigJson: null,
            capabilityConfigJson: null);

        var json = JsonSerializer.Serialize(payload);
        Assert.Contains("\"model\":\"grok-video-1.5\"", json);
        Assert.Contains("\"images\":[\"https://cdn.example/frame.png\"]", json);
        Assert.Contains("\"aspect_ratio\":\"9:16\"", json);
        Assert.Contains("\"duration\":6", json);
        Assert.Contains("\"size\":\"720P\"", json);
    }

    [Fact]
    public void Mapper_BuildsOmniFlashI2vPayload()
    {
        var payload = YEScaleVideoModelMapper.BuildSubmitRequest(
            "omni-flash",
            "  animate product shot  ",
            "https://cdn.example/frame.png",
            "16:9",
            "720P",
            4,
            providerConfigJson: null,
            capabilityConfigJson: """{"mode":"i2v(img_ref)"}""");

        var json = JsonSerializer.Serialize(payload);
        Assert.Contains("\"model\":\"omni-flash\"", json);
        Assert.Contains("\"prompt\":\"animate product shot\"", json);
        Assert.Contains("\"images\":[\"https://cdn.example/frame.png\"]", json);
        Assert.Contains("\"aspect_ratio\":\"16:9\"", json);
        Assert.Contains("\"mode\":\"i2v(img_ref)\"", json);
        Assert.DoesNotContain("\"duration\":", json);
        Assert.DoesNotContain("\"size\":", json);
    }

    [Fact]
    public void Mapper_UsesCapabilityConfigSnapshotForOmniFlashMode()
    {
        var payload = YEScaleVideoModelMapper.BuildSubmitRequest(
            "omni-flash",
            "animate product shot",
            "https://cdn.example/frame.png",
            "16:9",
            "720P",
            4,
            providerConfigJson: null,
            capabilityConfigJson: """{"mode":"v2v"}""");

        Assert.Equal("omni-flash", payload.Model);
        Assert.Equal("v2v", payload.Config.Mode);
    }
}
