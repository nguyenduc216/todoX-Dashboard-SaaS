using System.Text.Json;
using TodoX.EditVideoTool.Projects;

namespace TodoX.EditVideoTool.Tests;

/// <summary>"Tạo video từ sản phẩm": the agent's script, model choices and AI task answers as the app reads them.</summary>
public sealed class ProductVideoMakerTests
{
    [Fact]
    public void Brief_labels_what_the_customer_typed_and_skips_empty_lines()
    {
        var brief = ProductVideoMaker.Brief(" Serum X ", "299k", "Thấm nhanh\nKhông nhờn", null, " ");
        Assert.Contains("Sản phẩm: Serum X", brief);
        Assert.Contains("Giá / ưu đãi: 299k", brief);
        Assert.Contains("Điểm nổi bật: Thấm nhanh\nKhông nhờn", brief.Replace("\r\n", "\n"));
        Assert.DoesNotContain("Liên hệ", brief);
        Assert.DoesNotContain("Yêu cầu thêm", brief);
    }

    [Fact]
    public void Scenes_and_models_are_read_from_the_server_answers()
    {
        var script = JsonDocument.Parse("""{"scenes":[{"seconds":6,"voice":"Chào","imagePrompt":"bottle","motionPrompt":"orbit","purpose":"hook"}]}""").RootElement;
        var scene = Assert.Single(ProductVideoMaker.Scenes(script));
        Assert.Equal((6, "Chào", "bottle", "orbit", "hook"), (scene.Seconds, scene.Voice, scene.ImagePrompt, scene.MotionPrompt, scene.Purpose));

        var models = JsonDocument.Parse("""
            {"models":[
              {"model":"img1","name":"Ảnh 1","kind":"image","ratios":["9:16","1:1"],"abilities":["withReference"]},
              {"model":"vid1","name":"Video 1","kind":"video","durations":["5","10"],"abilities":["startImage"]},
              {"model":"vid2","name":"Video 2","kind":"video","available":false}
            ]}
            """).RootElement;
        var image = Assert.Single(ProductVideoMaker.Models(models, "image"));
        Assert.True(ProductVideoMaker.PhotoAsReference(image));
        Assert.Equal("vid1", Assert.Single(ProductVideoMaker.Models(models, "video")).Model);
    }

    [Theory]
    [InlineData(4, "5")]
    [InlineData(6, "10")]
    [InlineData(14, "10")]
    public void A_scene_gets_the_shortest_listed_length_that_holds_it(int seconds, string expected) =>
        Assert.Equal(expected, ProductVideoMaker.PickDuration(["10s", "5"], seconds) switch { "10s" => "10", var d => d });

    [Fact]
    public void Options_are_sent_only_when_the_model_lists_them()
    {
        Assert.Equal("9:16", ProductVideoMaker.Listed(["16:9", "9:16"], "9:16"));
        Assert.Null(ProductVideoMaker.Listed(["16:9", "1:1"], "9:16"));
        Assert.Equal("16:9", ProductVideoMaker.Listed(["16:9"], "9:16"));
        Assert.Null(ProductVideoMaker.PickDuration([], 5));
    }

    [Fact]
    public void Motion_prompts_lose_the_spoken_line()
    {
        var silent = ProductVideoMaker.SilentMotion("SHOT: slow push-in. SPEECH: Language vi-VN; exact spoken dialogue: \"Mua ngay\"; finish by 3s. TIMELINE: hold.");
        Assert.DoesNotContain("Mua ngay", silent);
        Assert.Contains("SHOT: slow push-in.", silent);
        Assert.Contains("TIMELINE: hold.", silent);
        Assert.EndsWith("the narration is added later.", silent);
        Assert.StartsWith("Gentle orbit.", ProductVideoMaker.SilentMotion("Gentle orbit."));
    }

    [Fact]
    public void Task_answers_give_task_status_and_file()
    {
        const string running = """{"task":"6f1c0e3a-1111-4222-8333-444455556666","kind":"video","status":"running","files":[]}""";
        const string done = """{"task":"6f1c0e3a-1111-4222-8333-444455556666","status":"succeeded","files":["C:\\p\\06-ai\\a.mp4"]}""";
        Assert.Equal("6f1c0e3a-1111-4222-8333-444455556666", ProductVideoMaker.TaskOf(running));
        Assert.Equal("running", ProductVideoMaker.StatusOf(running));
        Assert.Null(ProductVideoMaker.FileOf(running));
        Assert.Equal(@"C:\p\06-ai\a.mp4", ProductVideoMaker.FileOf(done));
        Assert.Null(ProductVideoMaker.TaskOf("Không đủ điểm TodoX."));
    }
}
