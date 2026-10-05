using System.Text.Json;
using TodoX.Web.Services.PromptAssistant;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class ServicePromptAssistantJsonRepairTests
{
    private static ServicePromptProviderResponse Respond(string content) => new(
        content,
        PromptTokens: 10,
        CompletionTokens: 20,
        TotalTokens: 30,
        SanitizedRawResponse: "raw");

    private const string ValidJson = """{"title":"ok","scenes":[{"scene_no":1,"voice":"a","motion":"b"}]}""";
    private const string TruncatedJson = """{"title":"ok","scenes":[{"scene_no":1,"voice":"broken""";

    [Fact]
    public async Task ValidInitialJson_PerformsNoRepair_AndCallsProviderOnce()
    {
        var calls = new List<string>();
        var outcome = await ServicePromptAssistantService.RunGenerationRepairLoopAsync(
            input => { calls.Add(input); return Task.FromResult(Respond(ValidJson)); },
            "user request", 1);

        Assert.Equal(0, outcome.RepairAttempts);
        Assert.Single(calls);
        Assert.Equal("user request", calls[0]);
        Assert.NotNull(outcome.GeneratedJson);
    }

    private static async Task<(bool Success, int RepairAttempts, int ProviderCalls, string? GeneratedJson, string? ErrorCode, string? ParserError)> RunScriptedAsync(
        IReadOnlyList<string> scripted, int maxRepairAttempts)
    {
        var index = 0;
        var calls = 0;
        try
        {
            var outcome = await ServicePromptAssistantService.RunGenerationRepairLoopAsync(
                _ => { calls++; return Task.FromResult(Respond(scripted[Math.Min(index++, scripted.Count - 1)])); },
                "user request", maxRepairAttempts);
            return (true, outcome.RepairAttempts, calls, outcome.GeneratedJson, null, null);
        }
        catch (ServicePromptProviderException ex)
        {
            return (false, 0, calls, null, ex.Code, ex.ParserErrorMessage);
        }
    }

    [Fact]
    public async Task InitialMalformed_RepairedValid_SucceedsWithOneRepairAttempt()
    {
        var (success, repairs, calls, generatedJson, errorCode, _) = await RunScriptedAsync([TruncatedJson, ValidJson], 1);

        Assert.True(success);
        Assert.Equal(1, repairs);
        Assert.Equal(2, calls);
        Assert.Contains("\"title\"", generatedJson);
    }

    [Fact]
    public async Task InitialMalformed_RepairStillMalformed_PreservesMalformedErrorCode()
    {
        var (success, _, calls, _, errorCode, parserError) = await RunScriptedAsync([TruncatedJson, TruncatedJson], 1);

        Assert.False(success);
        Assert.Equal("generated_json_malformed", errorCode);
        Assert.Equal(2, calls);
        Assert.NotNull(parserError);
    }

    [Fact]
    public async Task MaxRepairAttemptsZero_DoesNotCallRepair()
    {
        var (success, repairs, calls, _, errorCode, _) = await RunScriptedAsync([TruncatedJson, ValidJson], 0);

        Assert.False(success);
        Assert.Equal("generated_json_malformed", errorCode);
        Assert.Equal(1, calls);
        Assert.Equal(0, repairs);
    }

    [Fact]
    public async Task MaxRepairAttemptsOne_PerformsExactlyOneRepairCall()
    {
        var index = 0;
        var calls = 0;
        var scripted = new[] { TruncatedJson, TruncatedJson, ValidJson };
        try
        {
            await ServicePromptAssistantService.RunGenerationRepairLoopAsync(
                _ => { calls++; return Task.FromResult(Respond(scripted[Math.Min(index++, scripted.Length - 1)])); },
                "user request", 1);
        }
        catch (ServicePromptProviderException ex)
        {
            Assert.Equal("generated_json_malformed", ex.Code);
        }

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RepairRequestContainsMalformedContentAndParserErrorAndInstructions()
    {
        var capturedInputs = new List<string>();
        var index = 0;
        var scripted = new[] { TruncatedJson, ValidJson };
        await ServicePromptAssistantService.RunGenerationRepairLoopAsync(
            input => { capturedInputs.Add(input); return Task.FromResult(Respond(scripted[index++])); },
            "user request", 1);

        Assert.Equal("user request", capturedInputs[0]);
        var repairInput = capturedInputs[1];
        Assert.Contains("The previous response is incomplete or invalid JSON.", repairInput, StringComparison.Ordinal);
        Assert.Contains("Return valid JSON only.", repairInput, StringComparison.Ordinal);
        Assert.Contains("parser error:", repairInput, StringComparison.Ordinal);
        Assert.Contains(TruncatedJson, repairInput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyProviderContent_IsNotRepaired()
    {
        var calls = 0;
        var error = await Assert.ThrowsAsync<ServicePromptProviderException>(() =>
            ServicePromptAssistantService.RunGenerationRepairLoopAsync(
                _ => { calls++; return Task.FromResult(Respond("")); },
                "user request", 3));

        Assert.Equal("generated_json_malformed", error.Code);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ProviderExceptionDuringRepair_PropagatesProviderTaxonomy()
    {
        var calls = 0;
        var error = await Assert.ThrowsAsync<ServicePromptProviderException>(() =>
            ServicePromptAssistantService.RunGenerationRepairLoopAsync(
                _ =>
                {
                    calls++;
                    if (calls == 1) return Task.FromResult(Respond(TruncatedJson));
                    throw new ServicePromptProviderException("gommo_stream_hard_timeout", "Stream hard timeout.", "{}");
                },
                "user request", 1));

        Assert.Equal("gommo_stream_hard_timeout", error.Code);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ProductionRegression_TruncatedMidString_RepairedIntoValidJson()
    {
        var scene = new
        {
            scene_no = 1,
            voice = "Khả năng tự học là năng lực quan trọng nhất đời",
            motion = "camera pan",
            image_prompt = "A person in a store",
            duration_seconds = 8,
            shot_count = 1
        };
        var validFull = JsonSerializer.Serialize(new
        {
            title = "demo",
            scene_count = 1,
            duration = 8,
            total_shot_count = 1,
            scenes = new[] { scene }
        });
        var truncated = validFull[..^40];
        Assert.False(IsCompleteJson(truncated));

        var index = 0;
        var scripted = new[] { truncated, validFull };
        var outcome = await ServicePromptAssistantService.RunGenerationRepairLoopAsync(
            _ => Task.FromResult(Respond(scripted[index++])),
            "user request", 1);

        Assert.Equal(1, outcome.RepairAttempts);
        Assert.Equal(2, outcome.RawResponses.Count);
        using var document = JsonDocument.Parse(outcome.GeneratedJson);
        Assert.Equal("demo", document.RootElement.GetProperty("title").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("scene_count").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("scenes").GetArrayLength());
    }

    private static bool IsCompleteJson(string json)
    {
        try
        {
            using var _ = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
