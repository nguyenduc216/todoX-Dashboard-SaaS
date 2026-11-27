using TodoX.Web.Services.EditVideoTool;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>The EditVideo app's product video: the 79AI script agent's scenes become image / motion / narration steps.</summary>
public sealed class EditVideoAgentScriptTests
{
    private static readonly TodoXVideoPromptParser Parser = new();

    [Fact]
    public void Scenes_keep_prompts_and_narration_and_lengths_stay_in_range()
    {
        const string json = """
            {"video_title":"Serum A","cta":"Mua ngay","scenes":[
              {"scene_purpose":"hook","duration_seconds":2,"image_prompt":"Serum bottle on marble","motion_prompt":"slow orbit","voice":"Da khô?"},
              {"duration_seconds":30,"image_prompt":"Model applying serum","voice":"Thấm nhanh trong 10 giây."},
              {"duration_seconds":6,"image_prompt":"","voice":"bỏ cảnh này"}
            ]}
            """;

        var script = EditVideoAgentService.ToScript(Guid.NewGuid(), json, Parser)!;

        Assert.Equal(("Serum A", "Mua ngay"), (script.Title, script.Cta));
        Assert.Equal(2, script.Scenes.Count);
        Assert.Equal([4, 15], script.Scenes.Select(s => s.Seconds));
        Assert.Equal(19, script.TotalSeconds);
        Assert.Equal(("hook", "slow orbit", "Da khô?"), (script.Scenes[0].Purpose, script.Scenes[0].MotionPrompt, script.Scenes[0].Voice));
        Assert.False(string.IsNullOrWhiteSpace(script.Scenes[1].MotionPrompt));
        Assert.Equal(2, script.Scenes[1].Index);
    }

    [Fact]
    public void A_script_without_usable_scenes_is_refused() =>
        Assert.Null(EditVideoAgentService.ToScript(Guid.NewGuid(), """{"scenes":[{"image_prompt":"x"}]}""", Parser));

    [Theory]
    [InlineData(null, "Serum", 30, "Chọn kiểu video.")]
    [InlineData("4b0c2f3e-0000-4000-8000-000000000001", " ", 30, "Nhập thông tin sản phẩm.")]
    [InlineData("4b0c2f3e-0000-4000-8000-000000000001", "Serum", 5, "Độ dài video từ 10 đến 120 giây.")]
    [InlineData("4b0c2f3e-0000-4000-8000-000000000001", "Serum", 30, null)]
    public void Requests_are_checked(string? service, string brief, int seconds, string? expected) =>
        Assert.Equal(expected, EditVideoAgentService.Validate(new EditVideoAgentScriptRequest(service is null ? null : Guid.Parse(service), brief, seconds, "9:16")));
    [Fact]
    public void A_script_in_the_claude_back_up_shape_reads_like_the_agent_one()
    {
        const string json = """
            {"video_title":"Serum A","cta":"Gọi 0900","scenes":[
              {"scene_purpose":"hook","duration_seconds":6,"image_prompt":"Serum bottle on marble","motion_prompt":"slow push-in","tts_text":"Da khô mỗi sáng?"},
              {"scene_purpose":"cta","duration_seconds":5,"image_prompt":"Bottle with flowers","motion_prompt":"gentle orbit","tts_text":"Gọi ngay hôm nay."}
            ]}
            """;

        var script = EditVideoAgentService.ToScript(Guid.NewGuid(), json, Parser)! with { Source = "claude" };

        Assert.Equal(2, script.Scenes.Count);
        Assert.Equal(11, script.TotalSeconds);
        Assert.Equal(("Da khô mỗi sáng?", "slow push-in"), (script.Scenes[0].Voice, script.Scenes[0].MotionPrompt));
        Assert.Equal("claude", script.Source);
    }

    [Fact]
    public void The_claude_request_carries_the_brief_length_and_frame()
    {
        var prompt = EditVideoClaudeScriptWriter.UserPrompt("Video bán hàng", null, "Sản phẩm: Serum X", 30, "16:9");
        Assert.Contains("Kiểu video: Video bán hàng", prompt);
        Assert.Contains("khoảng 30 giây", prompt);
        Assert.Contains("Khung hình: 16:9", prompt);
        Assert.Contains("Sản phẩm: Serum X", prompt);
        Assert.DoesNotContain("Mô tả kiểu video", prompt);
    }
}
