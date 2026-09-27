using System.Text.Json;
using TodoX.Web.Services.PromptAssistant;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class PromptAssistantVideoOutputValidatorTests
{
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.2)]
    [InlineData(1.4)]
    [InlineData(1.5)]
    [InlineData(1.6)]
    [InlineData(1.85)]
    [InlineData(1.9)]
    [InlineData(2.0)]
    [InlineData(2.5)]
    public void AcceptsAnyPositiveNumericTtsRate(double speed)
    {
        using var prompt = JsonDocument.Parse(CreatePrompt(4, speed));

        Assert.Empty(PromptAssistantVideoOutputValidator.Validate(prompt.RootElement));
    }

    [Theory]
    [InlineData(3, 1.0, "duration_out_of_range")]
    [InlineData(9, 1.0, "duration_out_of_range")]
    [InlineData(4, 0.0, "tts_rate_invalid")]
    [InlineData(4, -0.5, "tts_rate_invalid")]
    public void RejectsDurationOrTtsSpeedOutsideAllowedRange(int duration, double speed, string expectedCode)
    {
        using var prompt = JsonDocument.Parse(CreatePrompt(duration, speed));

        Assert.Contains(PromptAssistantVideoOutputValidator.Validate(prompt.RootElement), error => error.Code == expectedCode);
    }

    [Theory]
    [InlineData("\"1.5\"")]
    [InlineData("null")]
    public void RejectsNonNumericTtsRate(string ttsRateJson)
    {
        using var prompt = JsonDocument.Parse(CreatePromptWithTtsRateJson(ttsRateJson));

        Assert.Contains(PromptAssistantVideoOutputValidator.Validate(prompt.RootElement), error => error.Code == "tts_rate_invalid");
    }

    [Fact]
    public void RejectsMissingTtsRate()
    {
        using var prompt = JsonDocument.Parse("""
            {"scenes":[{"duration_seconds":4,"image_prompt":"A clear visual.","motion_prompt":"Move naturally.","voice":"Narration."}]}
            """);

        Assert.Contains(PromptAssistantVideoOutputValidator.Validate(prompt.RootElement), error => error.Code == "tts_rate_invalid");
    }

    [Fact]
    public void AcceptsProductionGenerationTtsRates()
    {
        using var prompt = JsonDocument.Parse("""
            {"scenes":[
              {"duration_seconds":4,"image_prompt":"Scene 1","motion_prompt":"Move naturally.","voice":"Narration 1","tts_rate":1.4},
              {"duration_seconds":4,"image_prompt":"Scene 2","motion_prompt":"Move naturally.","voice":"Narration 2","tts_rate":1.5},
              {"duration_seconds":4,"image_prompt":"Scene 3","motion_prompt":"Move naturally.","voice":"Narration 3","tts_rate":1.5},
              {"duration_seconds":4,"image_prompt":"Scene 4","motion_prompt":"Move naturally.","voice":"Narration 4","tts_rate":1.5}
            ]}
            """);

        Assert.Empty(PromptAssistantVideoOutputValidator.Validate(prompt.RootElement));
    }

    [Fact]
    public void RejectsMissingSceneAndRequiredPromptFields()
    {
        using var missingScenes = JsonDocument.Parse("{}");
        using var missingFields = JsonDocument.Parse("""
            {"scenes":[{"duration_seconds":4,"tts_rate":1.0}]}
            """);

        Assert.Contains(PromptAssistantVideoOutputValidator.Validate(missingScenes.RootElement), error => error.Path == "$.scenes");
        var errors = PromptAssistantVideoOutputValidator.Validate(missingFields.RootElement);
        Assert.Contains(errors, error => error.Path == "$.scenes[0].image_prompt");
        Assert.Contains(errors, error => error.Path == "$.scenes[0].motion_prompt");
        Assert.Contains(errors, error => error.Path == "$.scenes[0].voice");
    }

    private static string CreatePrompt(int duration, double speed)
        => $$"""
           {"scenes":[{"duration_seconds":{{duration}},"image_prompt":"A clear visual.","motion_prompt":"Move naturally.","voice":"Narration.","tts_rate":{{speed}}}]}
           """;

    private static string CreatePromptWithTtsRateJson(string ttsRateJson)
        => $$"""
           {"scenes":[{"duration_seconds":4,"image_prompt":"A clear visual.","motion_prompt":"Move naturally.","voice":"Narration.","tts_rate":{{ttsRateJson}}}]}
           """;
}
