using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TodoX.Web.Components.Pages;
using TodoX.Web.Models;
using TodoX.Web.Services.Platform;
using Xunit;

namespace TodoX.Web.Tests;

// Regression: the deferred bootstrap (1101084) stopped applying the service's job defaults when a new RVideo job opens.
public sealed class RVideoServiceDefaultsOnCreateTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Guid ServiceGuid = Guid.NewGuid();

    public class FakeCatalog : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "ListAsync" => Task.FromResult<IReadOnlyList<CoreServiceView>>([Service()]),
            _ => throw new InvalidOperationException(method.Name)
        };
    }

    private static CoreServiceView Service() => new(ServiceGuid, "RVIDEO_TEST", "Test", "rvideo", null, null, null,
        JsonSerializer.SerializeToElement(new { }),
        JsonSerializer.SerializeToElement(new
        {
            version = 1,
            type = "service_job_defaults",
            aspectRatio = "9:16",
            resolution = "1080p",
            totalSeconds = 32,
            sceneSeconds = 6,
            executionMode = "auto",
            characterMode = "upload",
            useReferenceImageForAllScenes = true
        }),
        Array.Empty<CoreServicePriceView>(), true, 1);

    private static async Task<RenderVideoJobs> LoadAsync(string prompt)
    {
        var page = new RenderVideoJobs { ServiceId = ServiceGuid };
        typeof(RenderVideoJobs).GetProperty("CoreCatalog", Private)!.SetValue(page, DispatchProxy.Create<ICoreServiceCatalogService, FakeCatalog>());
        typeof(RenderVideoJobs).GetProperty("Logger", Private)!.SetValue(page, NullLogger<RenderVideoJobs>.Instance);
        typeof(RenderVideoJobs).GetProperty("AuthState", Private)!.SetValue(page, new TodoX.Web.Services.AuthStateService(null!, null!, NullLogger<TodoX.Web.Services.AuthStateService>.Instance));
        typeof(RenderVideoJobs).GetField("_prompt", Private)!.SetValue(page, prompt);
        try { await (Task)typeof(RenderVideoJobs).GetMethod("LoadSupplementalAsync", Private)!.Invoke(page, [])!; }
        catch (InvalidOperationException) { } // no render handle outside a renderer: only the final repaint is skipped
        return page;
    }

    private static object? Field(RenderVideoJobs page, string name) => typeof(RenderVideoJobs).GetField(name, Private)!.GetValue(page);

    [Fact]
    public async Task NewJobLoadsTheServiceDefaults()
    {
        var page = await LoadAsync("");

        Assert.Equal("9:16", Field(page, "_aspectRatio"));
        Assert.Equal("1080p", Field(page, "_resolution"));
        Assert.Equal(32, Field(page, "_totalSeconds"));
        Assert.Equal(6, Field(page, "_sceneSeconds"));
        Assert.Equal(RVideoExecutionModes.Auto, Field(page, "_executionMode"));
        Assert.Equal(RVideoCharacterModes.Upload, Field(page, "_characterMode"));
        Assert.Equal(false, Field(page, "_skipCharacter"));
        Assert.Equal(true, Field(page, "_useReferenceImageForAllScenes"));
    }

    [Fact]
    public async Task DefaultsDoNotOverwriteAFormThatAlreadyHasAPrompt()
    {
        var page = await LoadAsync("existing prompt");

        Assert.Equal("16:9", Field(page, "_aspectRatio"));
        Assert.Equal("720p", Field(page, "_resolution"));
        Assert.Equal(16, Field(page, "_totalSeconds"));
    }
}
