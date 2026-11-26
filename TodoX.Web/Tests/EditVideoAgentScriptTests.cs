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
}
