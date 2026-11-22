using TodoX.EditVideo.Planning;

namespace TodoX.EditVideo.Planning.Tests;

/// <summary>Talking-head / real-estate look: punch-in cuts, big headlines, logo, TikTok captions.</summary>
public sealed class PlanHighlightsTests
{
    private static readonly ClipInput[] Clips = [new("c0", 120, 1080, 1920, HasVideo: true, HasAudio: true)];

    private static AutoEditPlan Plan() => EditPlanner.Plan(new PlanRequest(Clips, TargetSeconds: 30));

    [Fact]
    public void Scenes_can_punch_in_and_cut_keeps_or_changes_it()
    {
        var plan = PlanEditor.Apply(Plan(), Clips, new PlanEdit("arrange",
            Scenes: [new PlanSceneInput("c0", 0, 4), new PlanSceneInput("c0", 5, 3, Zoom: 0.18)], TransitionSeconds: 0));
        Assert.Equal([0d, 0.18], plan.Clips.Select(c => c.Zoom));
        plan = PlanEditor.Apply(plan, Clips, new PlanEdit("cut", Scene: 1, Zoom: 0.12));
        Assert.Equal(0.12, plan.Clips[0].Zoom);
        Assert.Throws<ArgumentException>(() => PlanEditor.Apply(plan, Clips, new PlanEdit("cut", Scene: 1, Zoom: 0.9)));
    }

    [Fact]
    public void Headlines_are_checked_sorted_and_cut_to_the_video()
    {
        var plan = PlanEditor.Apply(Plan(), Clips, new PlanEdit("headlines", Headlines:
        [
            new PlanHeadline("TỪ A–Z", "", 8, 10),
            new PlanHeadline("PHƯỚC ĐÔNG NEW CITY", "Ai quản lý?", 1, 3.5),
            new PlanHeadline(" ", "", 2, 3)
        ]));
        Assert.Equal(["PHƯỚC ĐÔNG NEW CITY", "TỪ A–Z"], plan.Headlines!.Select(h => h.Text));
        Assert.Throws<ArgumentException>(() => PlanEditor.Apply(plan, Clips, new PlanEdit("headlines", Headlines: [new PlanHeadline("X", "", 5, 2)])));
        Assert.Null(PlanEditor.Apply(plan, Clips, new PlanEdit("headlines", Headlines: [])).Headlines);
    }

    [Fact]
    public void Logo_goes_in_a_corner_and_can_be_removed()
    {
        var plan = PlanEditor.Apply(Plan(), Clips, new PlanEdit("logo", Logo: true));
        Assert.Equal(new PlanLogo("logo", "top-right"), plan.Logo);
        Assert.Equal("bottom-left", PlanEditor.Apply(plan, Clips, new PlanEdit("logo", LogoCorner: "bottom-left")).Logo!.Corner);
        Assert.Null(PlanEditor.Apply(plan, Clips, new PlanEdit("logo", Logo: false)).Logo);
    }

    [Fact]
    public void Tiktok_is_a_caption_style() =>
        Assert.Equal("tiktok", PlanEditor.Apply(Plan(), Clips, new PlanEdit("captions", CaptionStyle: "tiktok")).Style.CaptionStyle);

    [Fact]
    public void Sparkle_is_a_caption_style() =>
        Assert.Equal("sparkle", PlanEditor.Apply(Plan(), Clips, new PlanEdit("captions", CaptionStyle: "sparkle")).Style.CaptionStyle);

    [Fact]
    public void Cut_sets_and_clears_the_cut_effect()
    {
        var plan = PlanEditor.Apply(Plan(), Clips, new PlanEdit("cut", Scene: 1, Fx: "Whip"));
        Assert.Equal("whip", plan.Clips[0].Fx);
        Assert.Equal("whip", PlanEditor.Apply(plan, Clips, new PlanEdit("cut", Scene: 1, Zoom: 0.1)).Clips[0].Fx);
        Assert.Null(PlanEditor.Apply(plan, Clips, new PlanEdit("cut", Scene: 1, Fx: "none")).Clips[0].Fx);
        Assert.Throws<ArgumentException>(() => PlanEditor.Apply(plan, Clips, new PlanEdit("cut", Scene: 1, Fx: "boom")));
    }
}
