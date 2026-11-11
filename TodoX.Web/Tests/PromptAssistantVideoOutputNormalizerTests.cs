using System.Text.Json;
using TodoX.Web.Services.PromptAssistant;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class PromptAssistantVideoOutputNormalizerTests
{
    // Shape returned by the 79AI "Người que 2d chibi" agent (job 067d60cb, 2026-11-11): fractional scene lengths, no motion_prompt.
    private const string ChibiAgentScript = """
        {"duration": 10.6, "scene_count": 2, "scenes": [
          {"scene": 1, "duration_seconds": 5.3, "speech_seconds": 4.8, "pause_seconds": 0.5, "tts_rate": 2.0,
           "voice": "Người gan dạ nhất thế gian kỳ thực chính là phụ nữ.", "image_prompt": "Hand-drawn chibi anime illustration...",
           "visual_core": "Người phụ nữ đứng trước hai lá bài úp", "cast_interaction": "cô nhìn thẳng vào hai lá bài",
           "light_fx": "spotlight-don", "object_fx": "vong-sao"},
          {"scene": 2, "duration_seconds": 3.2, "tts_rate": 2.0,
           "voice": "Họ dám đánh cược cả cuộc đời.", "image_prompt": "Hand-drawn chibi anime illustration..."}
        ]}
        """;

    [Fact]
    public void Provider_agent_script_passes_validation_after_normalization()
    {
        using var raw = JsonDocument.Parse(ChibiAgentScript);
        Assert.NotEmpty(PromptAssistantVideoOutputValidator.Validate(raw.RootElement));
        Assert.Empty(PromptAssistantVideoOutputNormalizer.ValidateNormalized(raw.RootElement));

        using var normalized = JsonDocument.Parse(PromptAssistantVideoOutputNormalizer.Normalize(ChibiAgentScript));
        var scenes = normalized.RootElement.GetProperty("scenes");
        Assert.Equal(5, scenes[0].GetProperty("duration_seconds").GetInt32());
        Assert.Equal(4, scenes[1].GetProperty("duration_seconds").GetInt32());
        Assert.Equal(9, normalized.RootElement.GetProperty("duration").GetInt32());
        var motion = scenes[0].GetProperty("motion_prompt").GetString()!;
        Assert.Contains("Người phụ nữ đứng trước hai lá bài úp", motion);
        Assert.Contains("spotlight-don", motion);
        Assert.False(string.IsNullOrWhiteSpace(scenes[1].GetProperty("motion_prompt").GetString()));
        // The agent's own wording is untouched.
        Assert.Equal("Họ dám đánh cược cả cuộc đời.", scenes[1].GetProperty("voice").GetString());
    }

    [Fact]
    public void A_script_that_follows_the_contract_is_unchanged()
    {
        const string json = """{"duration":12,"scenes":[{"duration_seconds":6,"image_prompt":"a","motion_prompt":"m","voice":"v","tts_rate":1.0},{"duration_seconds":6,"image_prompt":"b","video_prompt":"m2","voice":"v2","tts_rate":1.0}]}""";
        Assert.Same(json, PromptAssistantVideoOutputNormalizer.Normalize(json));
    }

    [Fact]
    public void Scenes_longer_than_the_maximum_still_fail()
    {
        const string json = """{"scenes":[{"duration_seconds":21.4,"image_prompt":"a","motion_prompt":"m","voice":"v","tts_rate":1.0}]}""";
        using var document = JsonDocument.Parse(json);
        Assert.Contains(PromptAssistantVideoOutputNormalizer.ValidateNormalized(document.RootElement), e => e.Code == "duration_out_of_range");
    }

    [Fact]
    public void Generation_normalizes_video_scripts_before_validating()
    {
        var service = File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
            "TodoX.Web", "Services", "PromptAssistant", "ServicePromptAssistantService.cs"));
        Assert.Contains("root => PromptAssistantVideoOutputNormalizer.ValidateNormalized(root)", service);
        Assert.Contains("generatedJson = PromptAssistantVideoOutputNormalizer.Normalize(generatedJson);", service);
    }
}
