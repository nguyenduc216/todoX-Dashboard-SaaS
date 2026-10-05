using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoPromptAssistantSettingsRegressionTests
{
    [Fact]
    public void CreativePromptSettingsUsePresetAndCustomDurationOptions()
    {
        var page = ReadRepoFile("Components", "Pages", "RenderVideoJobs.razor");
        var endpoint = ReadRepoFile("Services", "PromptAssistant", "PromptAssistantEndpoints.cs");

        Assert.Contains("Cài đặt dịch vụ", page);
        Assert.Contains("private static readonly int[] AiPromptDurations = [4, 6, 8, 10, 16, 24, 32, 40, 48, 60];", page);
        Assert.Contains("<MudSelectItem T=\"int\" Value=\"@AiPromptCustomDuration\">Khác</MudSelectItem>", page);
        Assert.Contains("MudNumericField T=\"int\" @bind-Value=\"_aiCustomDuration\"", page);
        Assert.Contains("private int ResolveAiPromptDuration()", page);
        Assert.DoesNotContain("private static readonly int[] AiPromptDurations = [5, 10, 15, 30, 60, 90];", page);
        Assert.Contains("if (request.Duration is <= 0) return \"duration must be greater than zero.\";", endpoint);
        Assert.DoesNotContain("duration must be one of 4, 6, 8, or 10 seconds.", endpoint);
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "TodoX.Web",
            Path.Combine(parts)));
}
