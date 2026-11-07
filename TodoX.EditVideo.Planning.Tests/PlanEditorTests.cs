using TodoX.EditVideo.Planning;

namespace TodoX.EditVideo.Planning.Tests;

public sealed class PlanEditorTests
{
    private static readonly ClipInput[] Clips =
    [
        new("c0", 10, 1920, 1080, HasVideo: true, HasAudio: true),
        new("c1", 6, 1920, 1080, HasVideo: true, HasAudio: true),
        new("c2", 3, 1920, 1080, HasVideo: true, HasAudio: false)
    ];

    private static AutoEditPlan Plan() =>
        EditPlanner.Plan(new PlanRequest(Clips, TargetSeconds: 15, Title: "Nhà phố An Đông", Script: "Mặt tiền rộng. Gần chợ."));

    [Fact]
    public void Cut_trims_one_scene_within_its_clip()
    {
        var plan = PlanEditor.Apply(Plan(), Clips, new PlanEdit("cut", Scene: 2, From: 1, Duration: 20));

        Assert.Equal(1, plan.Clips[1].From);
        Assert.Equal(5, plan.Clips[1].Duration); // clamped to the clip's end
        Assert.Throws<ArgumentException>(() => PlanEditor.Apply(plan, Clips, new PlanEdit("cut", Scene: 9)));
        Assert.Throws<ArgumentException>(() => PlanEditor.Apply(plan, Clips, new PlanEdit("cut", Scene: 1, From: 9.8)));
    }

    [Fact]
    public void Arrange_reorders_repeats_and_drops_scenes_and_sets_the_transition()
    {
        var plan = PlanEditor.Apply(Plan(), Clips, new PlanEdit("arrange",
            Scenes: [new("c2"), new("c0", 2, 3), new("c0", 6, 2)], Transition: "Slide", TransitionSeconds: 0.3));

        Assert.Equal(["c2", "c0", "c0"], plan.Clips.Select(c => c.Src));
        Assert.Equal((2, 3), (plan.Clips[1].From, plan.Clips[1].Duration));
        Assert.Equal("slide", plan.Style.Transition);
        Assert.Equal(0.3, plan.TransitionSeconds);
        Assert.Throws<ArgumentException>(() => PlanEditor.Apply(plan, Clips, new PlanEdit("arrange", Scenes: [new("c9")])));
        Assert.Throws<ArgumentException>(() => PlanEditor.Apply(plan, Clips, new PlanEdit("arrange", Transition: "spin")));
    }

    [Fact]
    public void Captions_from_exact_lines_or_a_script_and_never_past_the_end()
    {
        var plan = PlanEditor.Apply(Plan(), Clips, new PlanEdit("captions",
            Captions: [new("Dòng hai", 3, 4), new("Dòng một", 1, 2.5), new("Quá cuối", 500, 501)], CaptionStyle: "box"));

        Assert.Equal(["Dòng một", "Dòng hai"], plan.Captions.Select(c => c.Text));
        Assert.Equal("box", plan.Style.CaptionStyle);

        var scripted = PlanEditor.Apply(plan, Clips, new PlanEdit("captions", Script: "Một câu mới."));
        Assert.Equal("Một câu mới.", Assert.Single(scripted.Captions).Text);
        Assert.Empty(PlanEditor.Apply(plan, Clips, new PlanEdit("captions", Script: "")).Captions);
    }

    [Fact]
    public void Titles_change_or_remove_intro_and_outro()
    {
        var plan = PlanEditor.Apply(Plan(), Clips, new PlanEdit("titles", Subtitle: "Quận 1", OutroText: "Gọi 0909"));

        Assert.Equal(("Nhà phố An Đông", "Quận 1"), (plan.Intro!.Title, plan.Intro.Subtitle));
        Assert.Equal("Gọi 0909", plan.Outro!.Text);

        var bare = PlanEditor.Apply(plan, Clips, new PlanEdit("titles", Title: "", OutroText: ""));
        Assert.Null(bare.Intro);
        Assert.Null(bare.Outro);
    }

    [Fact]
    public void Audio_mutes_clip_sound_under_music_unless_asked()
    {
        var plan = PlanEditor.Apply(Plan(), Clips, new PlanEdit("audio", Music: true, MusicVolume: 9));

        Assert.Equal(new PlanAudio("music", 1.5), plan.Music);
        Assert.All(plan.Clips, c => Assert.False(c.KeepAudio));

        var keep = PlanEditor.Apply(plan, Clips, new PlanEdit("audio", KeepClipAudio: true));
        Assert.Equal([true, true, false], keep.Clips.Select(c => c.KeepAudio).Take(3));
        Assert.Null(PlanEditor.Apply(keep, Clips, new PlanEdit("audio", Music: false)).Music);
    }

    [Fact]
    public void Look_uses_catalog_styles_and_hex_colors_only()
    {
        var plan = PlanEditor.Apply(Plan(), Clips, new PlanEdit("look", StyleId: "real-estate", AccentColor: "#112233"));

        Assert.Equal("real-estate", plan.Style.Id);
        Assert.Equal("#112233", plan.AccentColor);
        Assert.Throws<ArgumentException>(() => PlanEditor.Apply(plan, Clips, new PlanEdit("look", StyleId: "nope")));
        Assert.Throws<ArgumentException>(() => PlanEditor.Apply(plan, Clips, new PlanEdit("look", AccentColor: "red")));
        Assert.Throws<ArgumentException>(() => PlanEditor.Apply(plan, Clips, new PlanEdit("ffmpeg")));
    }
}
