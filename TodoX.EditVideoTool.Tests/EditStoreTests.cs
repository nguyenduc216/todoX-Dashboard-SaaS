using System.Text;
using System.Text.Json;
using TodoX.EditVideoTool.Media;

namespace TodoX.EditVideoTool.Tests;

public sealed class EditStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "todox-editvideo-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Edits_are_saved_and_the_latest_is_the_default()
    {
        var store = new EditStore(new AgentPaths(_root));
        Assert.Null(store.Find(null));

        var first = store.Create("9:16");
        first.Clips.Add(new EditClip(first.NextClipId(), @"D:\a.mp4", 10, 1920, 1080, true, true));
        store.Save(first);
        var second = new EditProject { Id = "v-second", Aspect = "16:9" };
        File.SetLastWriteTimeUtc(Path.Combine(_root, "edits", first.Id + ".json"), DateTime.UtcNow.AddMinutes(-5));
        store.Save(second);

        Assert.Equal("v-second", store.Find(null)!.Id);
        Assert.Equal("c0", Assert.Single(store.Find(first.Id)!.Clips).Id);
        Assert.Equal("c1", store.Find(first.Id)!.NextClipId());
        Assert.Null(store.Find(@"..\..\license"));
    }

    [Fact]
    public void Only_ids_and_sizes_are_sent_to_TodoX_never_paths()
    {
        var edit = new EditProject();
        edit.Clips.Add(new EditClip("c0", @"D:\Khách\Dự án An Đông\mat-tien.mp4", 10, 1920, 1080, true, true));

        var json = JsonSerializer.Serialize(edit.ClipMetadata());

        Assert.Contains("\"c0\"", json);
        Assert.DoesNotContain("mat-tien", json);
    }

    [Fact]
    public void Signed_plan_is_read_with_ids_and_scene_start_times()
    {
        using var signer = new TestSigner();
        var plan = new AutoEditPlan(1080, 1920, 30,
            [new PlanClip("c0", 0, 4, false), new PlanClip("c1", 1, 3, false)], 0.5,
            new PlanIntro("Tiêu đề", "", 2.5), null, [], null, null, "#FFBE2E",
            new PlanStyle("todox", "TodoX", "#FFBE2E", "#0B0D10", "#FFFFFF", "Arial", "karaoke", "mix", 0.08), 0);
        var document = signer.SignRaw(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        var read = PlanDocument.Read(document, signer.Verifier)!;

        Assert.Equal(["c0", "c1"], read.Clips.Select(c => c.Src));
        Assert.Equal([2.0, 5.5], PlanDocument.SceneStarts(read));
        Assert.Null(PlanDocument.Read(document + "x", signer.Verifier));
    }
}

public sealed class SkillRunStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "todox-editvideo-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Runs_get_short_ids_and_the_latest_is_the_default()
    {
        var store = new TodoX.EditVideoTool.Setup.SkillRunStore(new AgentPaths(_root));
        Assert.Null(store.Find(null));

        var first = store.Save(null, "state-1");
        var second = store.Save(null, "state-2");
        store.Save(first, "state-1b");

        Assert.Equal(("r1", "r2"), (first, second));
        Assert.Equal(("r1", "state-1b"), store.Find(null));
        Assert.Equal("state-2", store.Find("r2")!.Value.State);
        Assert.Null(store.Find("r9"));
    }
}
