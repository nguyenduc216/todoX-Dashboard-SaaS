using TodoX.EditVideoTool.Media;

namespace TodoX.EditVideoTool.Tests;

public sealed class EditPlannerTests
{
    private static MediaInfo Clip(string name, double seconds, bool audio = true) =>
        new($@"C:\clips\{name}.mp4", seconds, 1920, 1080, 30, HasVideo: true, HasAudio: audio);

    [Fact]
    public void Plan_hits_the_target_length_including_intro_outro_and_transitions()
    {
        var plan = EditPlanner.Plan(new AutoEditRequest(
            [Clip("a", 20), Clip("b", 20), Clip("c", 20), Clip("d", 20)],
            TargetSeconds: 25, Title: "Căn hộ A2", OutroText: "Gọi ngay"));

        Assert.Equal(25, plan.TotalSeconds, 2);
        Assert.Equal(4, plan.Clips.Count);
        Assert.All(plan.Clips, c => Assert.InRange(c.Duration, EditPlanner.MinClipSeconds, EditPlanner.MaxClipSeconds));
        Assert.Equal((1080, 1920), (plan.Width, plan.Height));
    }

    [Fact]
    public void Short_clips_are_used_whole_and_long_ones_take_the_rest()
    {
        var plan = EditPlanner.Plan(new AutoEditRequest([Clip("short", 2), Clip("long", 60)], TargetSeconds: 8));

        Assert.Equal(2, plan.Clips[0].Duration, 2);
        Assert.Equal(6.5, plan.Clips[1].Duration, 2);
        Assert.Equal(8, plan.TotalSeconds, 2);
        Assert.InRange(plan.Clips[1].From, 0.1, 60 - 6.5);
    }

    [Fact]
    public void Too_many_clips_for_the_target_keeps_an_even_subset()
    {
        var clips = Enumerable.Range(0, 30).Select(i => Clip($"c{i:00}", 10)).ToList();

        var plan = EditPlanner.Plan(new AutoEditRequest(clips, TargetSeconds: 10));

        Assert.True(plan.Clips.Count < 30);
        Assert.Equal(clips[0].Path, plan.Clips[0].Src);
        Assert.Equal(clips[^1].Path, plan.Clips[^1].Src);
        Assert.All(plan.Clips, c => Assert.True(c.Duration >= EditPlanner.MinClipSeconds - 0.01));
    }

    [Fact]
    public void Clip_audio_is_kept_only_without_music_or_voiceover()
    {
        var plain = EditPlanner.Plan(new AutoEditRequest([Clip("a", 10), Clip("silent", 10, audio: false)]));
        Assert.True(plain.Clips[0].KeepAudio);
        Assert.False(plain.Clips[1].KeepAudio);

        var withVoice = EditPlanner.Plan(new AutoEditRequest([Clip("a", 10)], VoiceoverPath: @"C:\v.mp3", VoiceoverSeconds: 12));
        Assert.False(withVoice.Clips[0].KeepAudio);
        Assert.NotNull(withVoice.Voiceover);
        Assert.True(withVoice.TotalSeconds >= 12);
    }

    [Fact]
    public void Long_voiceover_with_little_footage_replays_clips_instead_of_cutting_the_voice()
    {
        var plan = EditPlanner.Plan(new AutoEditRequest(
            [Clip("a", 3), Clip("b", 4)], TargetSeconds: 10, VoiceoverPath: @"C:\v.mp3", VoiceoverSeconds: 20));

        Assert.True(plan.TotalSeconds >= 20, $"total {plan.TotalSeconds}");
        Assert.True(plan.Clips.Count > 2);
    }

    [Theory]
    [InlineData("9:16", 1080, 1920)]
    [InlineData("16:9", 1920, 1080)]
    [InlineData("1:1", 1080, 1080)]
    [InlineData("4:5", 1080, 1350)]
    public void Aspect_maps_to_resolution(string aspect, int w, int h) => Assert.Equal((w, h), EditPlanner.Resolution(aspect));

    [Fact]
    public void No_usable_clips_is_a_clear_error()
    {
        var ex = Assert.Throws<ArgumentException>(() => EditPlanner.Plan(new AutoEditRequest([Clip("tiny", 0.4)])));
        Assert.Contains("clip", ex.Message);
    }

    [Fact]
    public void Captions_cover_the_body_in_order_and_split_long_sentences()
    {
        var caps = EditPlanner.BuildCaptions(
            "Không gian sống rộng rãi đầy ánh sáng tự nhiên suốt cả ngày dài. Gọi ngay!", 2, 20);

        Assert.True(caps.Count >= 2);
        Assert.Equal(2, caps[0].Start, 2);
        Assert.Equal(20, caps[^1].End, 2);
        for (var i = 1; i < caps.Count; i++)
        {
            Assert.Equal(caps[i - 1].End, caps[i].Start, 2);
        }

        Assert.All(caps, c => Assert.True(c.Text.Split(' ').Length <= 10));
        Assert.Empty(EditPlanner.BuildCaptions(null, 0, 10));
    }

    [Fact]
    public void Probe_parses_ffprobe_json_including_rotation()
    {
        const string json = """
            {"streams":[{"codec_type":"video","width":1920,"height":1080,"avg_frame_rate":"30000/1001",
              "side_data_list":[{"rotation":-90}]},{"codec_type":"audio"}],
             "format":{"duration":"12.345"}}
            """;

        var info = MediaProbe.Parse(@"C:\x.mp4", json);

        Assert.Equal(12.345, info.DurationSeconds, 3);
        Assert.Equal((1080, 1920), (info.Width, info.Height));
        Assert.Equal(29.97, info.Fps, 2);
        Assert.True(info.HasVideo && info.HasAudio);
    }

    [Fact]
    public void Render_events_are_parsed_and_noise_ignored()
    {
        Assert.Equal(42, RemotionRenderer.ParseEvent("""{"type":"progress","percent":42}""")!.Percent);
        Assert.Equal("boom", RemotionRenderer.ParseEvent("""{"type":"error","message":"boom"}""")!.Message);
        Assert.Null(RemotionRenderer.ParseEvent("Downloading Chrome..."));
        Assert.Null(RemotionRenderer.ParseEvent("{not json"));
    }
}
