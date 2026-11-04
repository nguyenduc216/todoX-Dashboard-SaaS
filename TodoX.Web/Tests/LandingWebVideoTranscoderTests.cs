using TodoX.Web.Services.Landing;
using TodoX.Web.Services.SharedMedia;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class LandingWebVideoTranscoderTests
{
    private static List<string> Args() =>
        LandingWebVideoTranscoder.BuildArguments("in.mp4", "out.mp4", new VideoTranscodeOptions()).ToList();

    private static string ValueAfter(List<string> args, string flag) => args[args.IndexOf(flag) + 1];

    [Fact]
    public void MovesIndexToFrontSoPlaybackStartsBeforeFullDownload()
        => Assert.Equal("+faststart", ValueAfter(Args(), "-movflags"));

    [Fact]
    public void CapsBitrateForSlowServerUplink()
    {
        var args = Args();
        Assert.Equal("2500k", ValueAfter(args, "-maxrate"));
        Assert.Equal("5000k", ValueAfter(args, "-bufsize"));
    }

    [Fact]
    public void CapsShorterSideAt720WithoutUpscaling()
    {
        var filter = ValueAfter(Args(), "-vf");
        Assert.Contains("min(720,iw)", filter);
        Assert.Contains("min(720,ih)", filter);
    }

    [Fact]
    public void AudioIsOptionalAndOutputIsH264()
    {
        var args = Args();
        Assert.Contains("0:a?", args);
        Assert.Equal("libx264", ValueAfter(args, "-c:v"));
        Assert.Equal("out.mp4", args[^1]);
    }
}
