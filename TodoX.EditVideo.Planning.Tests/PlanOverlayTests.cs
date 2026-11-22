using TodoX.EditVideo.Planning;

namespace TodoX.EditVideo.Planning.Tests;

/// <summary>Cutaways (B-roll) over a talking head: picture only, the main scenes and voice keep running.</summary>
public sealed class PlanOverlayTests
{
    private static readonly ClipInput[] Clips =
    [
        new("c0", 120, 1080, 1920, HasVideo: true, HasAudio: true),
        new("c1", 20, 1080, 1920, HasVideo: true, HasAudio: false),
        new("music", 200, 0, 0, HasVideo: false, HasAudio: true)
    ];

    private static AutoEditPlan Plan() => PlanEditor.Apply(EditPlanner.Plan(new PlanRequest(Clips[..1], TargetSeconds: 30)), Clips,
        new PlanEdit("arrange", Scenes: [new PlanSceneInput("c0", 0, 30)], TransitionSeconds: 0));

    private static AutoEditPlan Broll(params PlanOverlayInput[] overlays) =>
        PlanEditor.Apply(Plan(), Clips, new PlanEdit("broll", Overlays: overlays));

    [Fact]
    public void Broll_adds_sorted_cutaways_without_changing_the_scenes()
    {
        var plan = Broll(new PlanOverlayInput("c1", 10, 2, From: 3, Crop: "top", Fx: "whip"), new PlanOverlayInput("c1", 2, 1.5, Speed: 2));

        Assert.Equal(30, plan.TotalSeconds);
        Assert.Single(plan.Clips);
        Assert.Equal([2.0, 10.0], plan.Overlays!.Select(o => o.Start));
        Assert.Equal(2, plan.Overlays![0].Speed);
        Assert.Equal(("top", 0.75, "whip"), (plan.Overlays[1].Crop, plan.Overlays[1].Keep, plan.Overlays[1].Fx));
    }

    [Fact]
    public void Empty_list_removes_cutaways_and_bad_ones_are_refused()
    {
        Assert.Null(PlanEditor.Apply(Broll(new PlanOverlayInput("c1", 2, 1)), Clips, new PlanEdit("broll", Overlays: [])).Overlays);
        Assert.Throws<ArgumentException>(() => Broll(new PlanOverlayInput("c1", 2, 3), new PlanOverlayInput("c1", 4, 1)));   // overlap
        Assert.Throws<ArgumentException>(() => Broll(new PlanOverlayInput("c1", 2, 15, From: 0, Speed: 2)));                // past clip end
        Assert.Throws<ArgumentException>(() => Broll(new PlanOverlayInput("music", 2, 1)));                                 // no picture
        Assert.Throws<ArgumentException>(() => Broll(new PlanOverlayInput("c1", 2, 1, Crop: "left")));
        Assert.Throws<ArgumentException>(() => Broll(new PlanOverlayInput("c1", 2, 1, Keep: 0.3, Crop: "top")));
    }

    [Fact]
    public void Shortening_the_video_trims_cutaways_past_the_end()
    {
        var plan = Broll(new PlanOverlayInput("c1", 5, 4), new PlanOverlayInput("c1", 20, 4));
        var shorter = PlanEditor.Apply(plan, Clips, new PlanEdit("cut", Scene: 1, Duration: 7));

        var only = Assert.Single(shorter.Overlays!);
        Assert.Equal(2, only.Duration);
    }
}
