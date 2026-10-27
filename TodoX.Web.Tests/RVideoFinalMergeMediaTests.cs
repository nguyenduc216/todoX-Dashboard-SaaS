using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class LocalFfmpegFactAttribute : FactAttribute
{
    public LocalFfmpegFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TODOX_TEST_FFMPEG")))
            Skip = "Set TODOX_TEST_FFMPEG to run local FFmpeg integration fixtures.";
    }
}

public sealed class RVideoFinalMergeMediaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "todox-final-merge-" + Guid.NewGuid().ToString("N"));
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);
    private static string Ffmpeg => Environment.GetEnvironmentVariable("TODOX_TEST_FFMPEG")
        ?? throw new InvalidOperationException("Set TODOX_TEST_FFMPEG to a local FFmpeg executable; ffprobe must be alongside it.");
    public RVideoFinalMergeMediaTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void TimelineRejectsFreezeGapsMissingTimestampsAndTruncation()
    {
        Assert.Null(FinalMergeMedia.ValidateTimeline(Enumerable.Range(0, 50).Select(i => (decimal?)(i / 25m)).ToArray(), 2));
        Assert.NotNull(FinalMergeMedia.ValidateTimeline([0, 1, 1.96m], 2));
        Assert.NotNull(FinalMergeMedia.ValidateTimeline([0, .04m, null], 2));
        Assert.NotNull(FinalMergeMedia.ValidateTimeline([0, .04m, .04m], 2));
        Assert.NotNull(FinalMergeMedia.ValidateTimeline([0, .04m, .08m], 2));
    }

    private async Task<string> Fixture(string name, string codec = "libx264", string rate = "25", string timebase = "12800", bool audio = true, string size = "160x120")
    {
        var path = Path.Combine(_root, name + ".mp4");
        var args = new List<string> { "-y", "-v", "error", "-f", "lavfi", "-i", $"testsrc2=size={size}:rate={rate}:duration=2" };
        if (audio) args.AddRange(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2"]);
        args.AddRange(["-c:v", codec, "-pix_fmt", "yuv420p", "-video_track_timescale", timebase]);
        if (audio) args.AddRange(["-c:a", "aac"]);
        args.AddRange(["-t", "2", path]);
        var result = await FinalMergeMedia.RunAsync(Ffmpeg, args, Timeout, default);
        Assert.True(result.ExitCode == 0, result.Log);
        return path;
    }

    private async Task<(string Output, List<string> Reasons)> Merge(params string[] paths)
    {
        var concat = Path.Combine(_root, "concat.txt");
        var output = Path.Combine(_root, "final.mp4");
        await File.WriteAllLinesAsync(concat, VideoRenderMergeHandler.BuildTimedConcatLines(paths.Select(p => (p, 2))), new System.Text.UTF8Encoding(false));
        var reasons = new List<string>();
        await FinalMergeMedia.MergeAsync(Ffmpeg, _root, concat, output, paths.Select(p => new FinalMergeMedia.Input(p, 2)).ToArray(), paths.Length * 2, Timeout,
            reason => { reasons.Add(reason); return Task.CompletedTask; }, default);
        Assert.Null(await FinalMergeMedia.ValidateAsync(Ffmpeg, output, paths.Length * 2, Timeout, default));
        return (output, reasons);
    }

    [LocalFfmpegFact]
    public async Task HomogeneousInputsDecodeWithExpectedTimelineAndAudio()
    {
        var first = await Fixture("first");
        var result = await Merge(first, first);
        Assert.Empty(result.Reasons);
        Assert.True((await FinalMergeMedia.InspectAsync(Ffmpeg, result.Output, Timeout, default)).Audio);
    }

    [LocalFfmpegFact]
    public async Task DifferentCodecFpsTimebaseResolutionAndMissingAudioAreNormalized()
    {
        var first = await Fixture("first");
        var second = await Fixture("second", "mpeg4", "30", "30000", audio: false, size: "176x144");
        var result = await Merge(first, second);
        Assert.Contains("incompatible_input_streams", result.Reasons);
        var probe = await FinalMergeMedia.InspectAsync(Ffmpeg, result.Output, Timeout, default);
        Assert.Equal("h264", probe.Codec);
        Assert.True(probe.Audio);
        Assert.Equal(160, probe.Width);
    }

    [LocalFfmpegFact]
    public async Task CopyExitZeroWithWrongTimelineTriggersFallback()
    {
        var first = await Fixture("first");
        // A stale/incorrect concat manifest can still produce exit 0 and a truncated picture.
        var concat = Path.Combine(_root, "concat.txt");
        await File.WriteAllLinesAsync(concat, VideoRenderMergeHandler.BuildConcatLines([first]), new System.Text.UTF8Encoding(false));
        var output = Path.Combine(_root, "final.mp4");
        var copy = await FinalMergeMedia.RunAsync(Ffmpeg, VideoRenderMergeHandler.BuildCopyConcatArguments(concat, output, 4), Timeout, default);
        Assert.Equal(0, copy.ExitCode);
        Assert.NotNull(await FinalMergeMedia.ValidateAsync(Ffmpeg, output, 4, Timeout, default));
        var reasons = new List<string>();
        await FinalMergeMedia.MergeAsync(Ffmpeg, _root, concat, output, [new(first, 2), new(first, 2)], 4, Timeout,
            reason => { reasons.Add(reason); return Task.CompletedTask; }, default);
        Assert.NotEmpty(reasons);
        Assert.Null(await FinalMergeMedia.ValidateAsync(Ffmpeg, output, 4, Timeout, default));
    }

    [LocalFfmpegFact]
    public async Task InvalidInputCannotPublishOutput()
    {
        var invalid = Path.Combine(_root, "invalid.mp4");
        await File.WriteAllTextAsync(invalid, "invalid-media");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Merge(invalid));
    }

    [LocalFfmpegFact]
    public async Task ReproducesMixedTimebaseCopySuccessWithoutValidPictureTimeline()
    {
        var first = await Fixture("first");
        var second = await Fixture("second", timebase: "90000");
        var concat = Path.Combine(_root, "raw-concat.txt");
        await File.WriteAllLinesAsync(concat, VideoRenderMergeHandler.BuildTimedConcatLines(new[] { (first, 2), (second, 2) }), new System.Text.UTF8Encoding(false));
        var output = Path.Combine(_root, "raw.mp4");
        var copy = await FinalMergeMedia.RunAsync(Ffmpeg, VideoRenderMergeHandler.BuildCopyConcatArguments(concat, output, 4), Timeout, default);
        Assert.Equal(0, copy.ExitCode);
        Assert.NotNull(await FinalMergeMedia.ValidateAsync(Ffmpeg, output, 4, Timeout, default));
        var result = await Merge(first, second);
        Assert.Contains("incompatible_input_streams", result.Reasons);
    }
}
