using System.Text.Json;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services.Timelapse;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class TimelapseVideoDirectionTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void EveryGeneratedVideoEdgeMovesForward(int sceneCount)
    {
        var graph = TimelapseStageGraphBuilder.Build(sceneCount);

        Assert.NotEmpty(graph.VideoClips);
        Assert.All(graph.VideoClips, edge => Assert.True(edge.StartProgressPercent < edge.EndProgressPercent));
        Assert.Equal(graph.ImageProgressions.Count - 1, graph.VideoClips.Count);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(20, 40)]
    [InlineData(40, 60)]
    [InlineData(60, 80)]
    [InlineData(80, 100)]
    public void ForwardPairIsAccepted(int start, int end)
    {
        var result = TimelapseVideoDirectionValidator.Validate(
            start,
            end,
            Guid.NewGuid(),
            start,
            Guid.NewGuid(),
            end,
            Guid.NewGuid(),
            Guid.NewGuid());

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorCode);
    }

    [Theory]
    [InlineData(20, 0, "timelapse_video_direction_invalid")]
    [InlineData(20, 20, "timelapse_video_direction_invalid")]
    public void ReverseOrFlatPairIsRejected(int start, int end, string errorCode)
    {
        var result = TimelapseVideoDirectionValidator.Validate(
            start,
            end,
            Guid.NewGuid(),
            start,
            Guid.NewGuid(),
            end,
            Guid.NewGuid(),
            Guid.NewGuid());

        Assert.False(result.IsValid);
        Assert.Equal(errorCode, result.ErrorCode);
    }

    [Fact]
    public void WrongStagesAndMissingMediaAreRejected()
    {
        var wrongStart = TimelapseVideoDirectionValidator.Validate(
            0, 20, Guid.NewGuid(), 20, Guid.NewGuid(), 20, Guid.NewGuid(), Guid.NewGuid());
        var wrongEnd = TimelapseVideoDirectionValidator.Validate(
            0, 20, Guid.NewGuid(), 0, Guid.NewGuid(), 0, Guid.NewGuid(), Guid.NewGuid());
        var missingStart = TimelapseVideoDirectionValidator.Validate(
            0, 20, Guid.NewGuid(), 0, Guid.NewGuid(), 20, null, Guid.NewGuid());
        var missingEnd = TimelapseVideoDirectionValidator.Validate(
            0, 20, Guid.NewGuid(), 0, Guid.NewGuid(), 20, Guid.NewGuid(), null);

        Assert.Equal("timelapse_video_start_stage_mismatch", wrongStart.ErrorCode);
        Assert.Equal("timelapse_video_end_stage_mismatch", wrongEnd.ErrorCode);
        Assert.Equal("timelapse_video_missing_start_media", missingStart.ErrorCode);
        Assert.Equal("timelapse_video_missing_end_media", missingEnd.ErrorCode);
    }

    [Fact]
    public void RealVideoRequestPairBuilderKeepsEarlierImageFirst()
    {
        var json = TimelapseProviderRuntime.BuildVideoImagePairJson(
            new TimelapseProviderRuntime.TimelapseVideoImageDescriptor("start", "project", "https://example.test/0.png", "0.png"),
            new TimelapseProviderRuntime.TimelapseVideoImageDescriptor("end", "project", "https://example.test/20.png", "20.png"));
        using var document = JsonDocument.Parse(json);
        var images = document.RootElement.EnumerateArray().ToArray();

        Assert.Equal("https://example.test/0.png", images[0].GetProperty("url").GetString());
        Assert.Equal("https://example.test/20.png", images[1].GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("landscape_balcony_install_v1", "installation progression", "Never pull flooring apart")]
    [InlineData("landscape_garden_growth_v1", "Growth progression", "Never shrink plants")]
    [InlineData("landscape_balcony_hybrid_v1", "Hybrid progression", "Never remove flooring")]
    public void VideoPromptContainsForwardAndProfileRules(string profileCode, string profileRule, string forbiddenRule)
    {
        var prompt = TimelapsePromptResolver.ResolveVideoPrompt(
            new TimelapseJobSnapshot
            {
                ProfileCode = profileCode,
                ProfileName = profileCode
            },
            1,
            0,
            20);

        Assert.Contains("forward chronological progression", prompt);
        Assert.Contains("begin from the earlier-progress reference state", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("end at the later-progress reference state", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The scene must become progressively more complete", prompt);
        Assert.Contains("Never reverse construction or landscaping progress", prompt);
        Assert.Contains("Never dismantle completed flooring, deck, planters, fixtures or permanent elements", prompt);
        Assert.Contains("Do not remove elements that belong to the later stage", prompt);
        Assert.Contains("same building, architecture", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(profileRule, prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(forbiddenRule, prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReverseImageDependencyOrderRemainsDescendingWhileVideoEdgesStayForward()
    {
        var graph = TimelapseStageGraphBuilder.Build(5);

        Assert.Equal(new[] { 80, 60, 40, 20, 0 }, graph.GeneratedImageOrder);
        Assert.Equal(
            new[] { "0->20", "20->40", "40->60", "60->80", "80->100" },
            graph.VideoClips.Select(x => $"{x.StartProgressPercent}->{x.EndProgressPercent}"));
    }

    [Fact]
    public void VideoRequestContractStillUsesImagesOptionAndExistingEndpoints()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs"));

        Assert.Contains("[\"images\"] = imagesJson", source);
        Assert.Contains("DefaultVideoSubmitPath", source);
        Assert.Contains("DefaultVideoPollPath", source);
        Assert.Contains("_taskClient.SubmitAsync(request.Raw, ct)", source);
        Assert.DoesNotContain("YEScale", source, StringComparison.OrdinalIgnoreCase);
    }
}
