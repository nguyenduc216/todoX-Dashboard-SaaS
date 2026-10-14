using System.Reflection;
using TodoX.Web.Components.Pages;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoResolutionUiSyncTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static void Set(RenderVideoJobs page, string name, object? value) => typeof(RenderVideoJobs).GetField(name, Flags)!.SetValue(page, value);
    private static T Get<T>(RenderVideoJobs page, string name) => (T)typeof(RenderVideoJobs).GetField(name, Flags)!.GetValue(page)!;
    private static void Apply(RenderVideoJobs page) => typeof(RenderVideoJobs).GetMethod("ApplyVideoSettingsFromPrompt", Flags)!.Invoke(page, null);
    private static RenderVideoJobs Page(string promptResolution, string? persisted, string selected, bool dirty)
    {
        var page = new RenderVideoJobs();
        var parser = new TodoXVideoPromptParser();
        var prompt = "{\"resolution\":\"" + promptResolution + "\",\"scenes\":[]}";
        typeof(RenderVideoJobs).GetProperty("PromptParser", Flags)!.SetValue(page, parser);
        Set(page, "_prompt", prompt);
        Set(page, "_promptParseResult", parser.Parse(prompt));
        Set(page, "_persistedResolution", persisted);
        Set(page, "_resolution", selected);
        Set(page, "_resolutionDirty", dirty);
        return page;
    }

    [Theory]
    [InlineData("720p", "1080p")]
    [InlineData("1080p", "720p")]
    public void DirtySelectionThenSavedSelectionWinOverOldActivePrompt(string oldValue, string selected)
    {
        var page = Page(oldValue, oldValue, selected, true);
        Apply(page);
        Assert.Equal(selected, Get<string>(page, "_resolution"));
        Set(page, "_persistedResolution", selected);
        Set(page, "_resolutionDirty", false);
        var savedPrompt = (string)typeof(RenderVideoJobs).GetMethod("PersistPromptVideoSettings", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { Get<string>(page, "_prompt"), "9:16", selected, false })!;
        Assert.Equal(selected, new TodoXVideoPromptParser().Parse(savedPrompt).Model.Resolution);
        Apply(page);
        Assert.Equal(selected, Get<string>(page, "_resolution"));
        Assert.Equal(selected, Get<TodoXVideoPromptParseResult>(page, "_promptParseResult").Summary.Resolution);
    }

    [Fact]
    public void ValidSelectionRemovesStaleResolutionErrorWithoutChangingRawPrompt()
    {
        var page = Page("invalid", "720p", "1080p", true);
        var original = Get<string>(page, "_prompt");
        Apply(page);
        Assert.False(Get<TodoXVideoPromptParseResult>(page, "_promptParseResult").HasInvalidResolution);
        Assert.Equal(original, Get<string>(page, "_prompt"));
    }

    [Fact]
    public void InvalidPromptWithoutSavedOrDirtySelectionStillReportsError()
    {
        var page = Page("invalid", null, "720p", false);
        Apply(page);
        Assert.True(Get<TodoXVideoPromptParseResult>(page, "_promptParseResult").HasInvalidResolution);
    }

    [Fact]
    public void CheckAndResolutionChangeDoNotEnqueueOrReapplyPromptSettings()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TodoX.Web", "Components", "Pages", "RenderVideoJobs.razor"));
        var check = source[source.IndexOf("private Task CheckPromptAsync()", StringComparison.Ordinal)..source.IndexOf("private Task OpenRawPromptAsync()", StringComparison.Ordinal)];
        Assert.DoesNotContain("ApplyVideoSettingsFromPrompt", check);
        Assert.Contains("SynchronizeResolutionDiagnostics", check);
        var change = source[source.IndexOf("private Task OnResolutionChangedAsync", StringComparison.Ordinal)..source.IndexOf("private void ApplyVideoSettingsFromPrompt", StringComparison.Ordinal)];
        Assert.DoesNotContain("RenderAsync", change);
        Assert.DoesNotContain("Enqueue", change);
        Assert.Contains("_resolutionDirty = true", change);
        var input = source[source.IndexOf("private bool TryBuildSceneVideoRenderInput", StringComparison.Ordinal)..source.IndexOf("validationError = null;", source.IndexOf("private bool TryBuildSceneVideoRenderInput", StringComparison.Ordinal), StringComparison.Ordinal)];
        Assert.Contains("Resolution = _persistedResolution ?? _resolution", input);
    }
}
