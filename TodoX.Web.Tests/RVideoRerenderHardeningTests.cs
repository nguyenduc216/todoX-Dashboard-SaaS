using TodoX.Web.Services.VideoRender;
using System.Reflection;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>RVID-UI-V2-SCENE-RERENDER-HISTORY / TODOX-20261014-RVIDEO-RERENDER-HARDENING-001.</summary>
public sealed class RVideoRerenderHardeningTests
{
    [Fact]
    public void VideoVersionQueryProjectsPersistedErrorCode()
    {
        var sql = (string)typeof(SceneMediaVersioningService)
            .GetField("SelectSceneVideoVersionSql", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;
        Assert.Contains("error_code AS ErrorCode", sql);
        Assert.NotNull(typeof(SceneVideoVersionDto).GetProperty(nameof(SceneVideoVersionDto.ErrorCode)));
    }

    [Fact]
    public void ImageUnavailableEstimateFailsClosed()
    {
        Assert.False(RVideoSceneImageRenderEstimate.Unavailable.CanStart);
        Assert.Equal(0, RVideoSceneImageRenderEstimate.Unavailable.ImageRate);
    }

    [Fact]
    public void VideoUnavailableEstimateFailsClosed()
    {
        Assert.False(RVideoSceneVideoRenderEstimate.Unavailable.CanStart);
        Assert.Equal(0, RVideoSceneVideoRenderEstimate.Unavailable.VideoRate);
    }
}