using System.Text.Json;
using TodoX.Web.Services.PromptAssistant;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class PromptAssistantProductionRepairTests
{
    private static string Prompt(string image, string motion, string voice = "original") => JsonSerializer.Serialize(new
    {
        scenes = Enumerable.Range(1, 10).Select(i => new { scene_no = i, duration_seconds = 8, tts_rate = 1.0, voice, scene_purpose = "purpose", image_prompt = image, motion_prompt = motion })
    });
    private static ServicePromptProviderResponse Response(string text) => new(text, null, null, null, "raw");

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public async Task EmptyProductionFieldsRespectBudgetAndPreserveMetadata(int budget, bool success)
    {
        var calls = 0;
        var attempts = 0;
        var outcome = await ServicePromptAssistantService.RunGenerationRepairLoopAsync(
            _ => Task.FromResult(Response(++calls == 1 || !success ? Prompt("", "") : Prompt("real image", "real motion", "changed"))),
            "source", budget, validateOutput: PromptAssistantVideoOutputValidator.Validate, onRepairAttempt: n => attempts = n);
        using var json = JsonDocument.Parse(outcome.GeneratedJson);
        var errors = PromptAssistantVideoOutputValidator.Validate(json.RootElement);
        Assert.Equal(success && budget > 0 ? 0 : 20, errors.Count);
        Assert.Equal(budget, attempts);
        Assert.Equal(1 + budget, calls);
        Assert.Equal(10, json.RootElement.GetProperty("scenes").GetArrayLength());
        Assert.All(json.RootElement.GetProperty("scenes").EnumerateArray(), scene =>
        {
            Assert.Equal("original", scene.GetProperty("voice").GetString());
            Assert.Equal(8, scene.GetProperty("duration_seconds").GetInt32());
            Assert.Equal("purpose", scene.GetProperty("scene_purpose").GetString());
        });
    }

    [Fact]
    public async Task EmptyScenesFailAndCompleteOutputPasses()
    {
        using var empty = JsonDocument.Parse("{\"scenes\":[]}");
        Assert.Contains(PromptAssistantVideoOutputValidator.Validate(empty.RootElement), e => e.Code == "required_scenes");
        var calls = 0;
        var result = await ServicePromptAssistantService.RunGenerationRepairLoopAsync(
            _ => { calls++; return Task.FromResult(Response(Prompt("real image", "real motion"))); },
            "source", 1, validateOutput: PromptAssistantVideoOutputValidator.Validate);
        Assert.Equal(0, result.RepairAttempts);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FailedRepairRetainsAttemptCount()
    {
        var calls = 0;
        var attempts = 0;
        await Assert.ThrowsAsync<ServicePromptProviderException>(() => ServicePromptAssistantService.RunGenerationRepairLoopAsync(
            _ => ++calls == 1 ? Task.FromResult(Response(Prompt("", ""))) : throw new ServicePromptProviderException("missing_content", "empty", "raw"),
            "source", 1, validateOutput: PromptAssistantVideoOutputValidator.Validate, onRepairAttempt: n => attempts = n));
        Assert.Equal(1, attempts);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task EmptyScenesCanBeRepairedWithoutChangingReference()
    {
        var calls = 0;
        var result = await ServicePromptAssistantService.RunGenerationRepairLoopAsync(
            _ => Task.FromResult(Response(++calls == 1 ? "{\"character_reference_image\":\"original\",\"scenes\":[]}" : Prompt("image", "motion"))),
            "source", 1, validateOutput: PromptAssistantVideoOutputValidator.Validate);
        using var json = JsonDocument.Parse(result.GeneratedJson);
        Assert.Empty(PromptAssistantVideoOutputValidator.Validate(json.RootElement));
        Assert.Equal("original", json.RootElement.GetProperty("character_reference_image").GetString());
        Assert.Equal(1, result.RepairAttempts);
    }

    [Fact]
    public async Task RepairCannotChangeSceneCount()
    {
        var calls = 0;
        var result = await ServicePromptAssistantService.RunGenerationRepairLoopAsync(
            _ => Task.FromResult(Response(++calls == 1 ? Prompt("", "") : "{\"scenes\":[]}")),
            "source", 1, validateOutput: PromptAssistantVideoOutputValidator.Validate);
        using var json = JsonDocument.Parse(result.GeneratedJson);
        Assert.Equal(10, json.RootElement.GetProperty("scenes").GetArrayLength());
        Assert.Equal(20, PromptAssistantVideoOutputValidator.Validate(json.RootElement).Count);
    }

    [Fact]
    public void ReferenceCannotFillEmptyImagePrompt()
    {
        var json = PromptAssistantCharacterReferenceSynchronizer.Synchronize(
            "{\"scenes\":[{\"image_prompt\":\"\",\"main_character_present\":true}]}",
            new PromptAssistantCharacterReference("upload", "reference"));
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("", parsed.RootElement.GetProperty("scenes")[0].GetProperty("image_prompt").GetString());
    }
}
