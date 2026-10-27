using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using TodoX.Web.Components.Shared;
using TodoX.Web.Models;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoRecomposeUiTests
{
    public sealed class ProbeAction : RVideoRecomposeAction
    {
        [Parameter] public Action<ProbeAction> Capture { get; set; } = null!;
        protected override void OnInitialized() => Capture(this);
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken token, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }

    [Theory]
    [InlineData(RenderJobStatuses.Completed)]
    [InlineData(RenderJobStatuses.Failed)]
    public async Task SharedActionRendersCompletedFinalAndTracksTerminalResultWithoutEnqueueOnMount(string terminal)
    {
        var project = new VideoProjectDto { Id = 45, Status = VideoProjectStatuses.Completed, FinalVideoUrl = "/old-final.mp4" };
        var jobId = Guid.NewGuid();
        RenderJobDto? job = null;
        var requests = 0;
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobs = FinalMergeProxy.Create<IRenderJobService>((m, a) =>
        {
            Assert.Equal("ListByLogCodeAsync", m.Name);
            read.TrySetResult();
            return Task.FromResult<IReadOnlyList<RenderJobDto>>(job is null ? [] : [job]);
        });
        var finalization = FinalMergeProxy.Create<IRVideoProjectFinalizationService>((m, a) =>
        {
            Assert.Equal("TryEnqueueFinalMergeAsync", m.Name);
            Assert.Equal(RVideoProjectFinalizationContracts.TriggerManual, a[1]);
            requests++;
            job = new() { Id = jobId, JobType = RenderJobTypes.MergeProjectVideo, Status = RenderJobStatuses.Queued };
            return Task.FromResult(new RVideoProjectFinalizationResult(true, false, "enqueued", jobId, "test-request"));
        });
        using var services = new ServiceCollection().AddLogging().AddMudServices()
            .AddSingleton<NavigationManager, TestNavigation>().AddSingleton<IJSRuntime, NoJs>().AddSingleton(jobs).AddSingleton(finalization)
            .Configure<VideoRenderOptions>(o => o.PollIntervalSeconds = 1).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        ProbeAction? action = null;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<ProbeAction>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["Project"] = project, ["Capture"] = (Action<ProbeAction>)(value => action = value),
            ["OnReloadRequested"] = EventCallback.Factory.Create(this, () =>
            {
                if (job?.Status == RenderJobStatuses.Completed) project.FinalVideoUrl = "/new-final.mp4";
                if (job?.Status == terminal) refreshed.TrySetResult();
            })
        })));
        await read.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Drain the renderer queue after the read-only initial check.
        await renderer.Dispatcher.InvokeAsync(async () => { await Task.Yield(); });
        Assert.Equal(0, requests);
        Assert.Contains("Ghép lại video", System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await action!.SubmitAsync();
            await action.SubmitAsync(); // Active-job/double-click guard at the shared UI boundary.
        });
        Assert.Equal(1, requests);
        Assert.Equal("/old-final.mp4", project.FinalVideoUrl);
        job!.Status = terminal;
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var html = System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        if (terminal == RenderJobStatuses.Completed)
        {
            Assert.Equal("/new-final.mp4", project.FinalVideoUrl);
            Assert.Contains("thành công", html);
        }
        else
        {
            Assert.Equal("/old-final.mp4", project.FinalVideoUrl);
            Assert.Contains("thất bại", html);
            await renderer.Dispatcher.InvokeAsync(() => action!.SubmitAsync());
            Assert.Equal(2, requests);
        }
    }
}
