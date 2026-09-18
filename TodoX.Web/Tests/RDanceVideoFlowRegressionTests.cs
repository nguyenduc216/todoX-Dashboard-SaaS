using Xunit;

namespace TodoX.Web.Tests;

public sealed class RDanceVideoFlowRegressionTests
{
    [Fact]
    public void ImageInputsRenderImmediateLoadingStateAndRetryableFailures()
    {
        var create = ReadRepoFile("Components", "Pages", "RDanceJobCreate.razor");
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");

        foreach (var source in new[] { create, detail })
        {
            Assert.Contains("ImageInputState.Uploading", source);
            Assert.Contains("Đang tải hình ảnh...", source);
            Assert.Contains("IsImageInputActive", source);
            Assert.Contains("_imageInputState = ImageInputState.Failed", source);
            Assert.Contains("_imageUploadError", source);
            Assert.Contains("await InvokeAsync(StateHasChanged);", source);
        }
    }

    [Fact]
    public void MotionInputsRenderImmediateOperationSpecificLoadingStates()
    {
        var create = ReadRepoFile("Components", "Pages", "RDanceJobCreate.razor");
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");

        foreach (var source in new[] { create, detail })
        {
            Assert.Contains("MotionInputState.Resolving", source);
            Assert.Contains("MotionInputState.Uploading", source);
            Assert.Contains("Đang tải video...", source);
            Assert.Contains("Đang tải video lên...", source);
            Assert.Contains("await InvokeAsync(StateHasChanged);", source);
            Assert.Contains("role=\"status\" aria-live=\"polite\"", source);
        }
    }

    [Fact]
    public void ResultEmptyStateUsesCurrentInputsAndExistingRenderCommand()
    {
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");
        var start = detail.IndexOf("<div class=\"rdance-result-empty-state\">", StringComparison.Ordinal);
        var end = detail.IndexOf("else", start, StringComparison.Ordinal);
        var emptyState = detail[start..end];

        Assert.Contains("_job.PreparedReferenceUrl ?? _job.CharacterImageUrl", emptyState);
        Assert.Contains("_job.Prompt", emptyState);
        Assert.Contains("_job.MotionVideoUrl", emptyState);
        Assert.Contains("OnClick=\"ConfirmAndQueueAsync\"", emptyState);
        Assert.DoesNotContain("QueueRenderAsync", emptyState);

        Assert.Contains("justify-content: center", detail);
        Assert.Contains("align-items: center", detail);
        Assert.DoesNotContain("grid-template-rows: minmax(0, 1fr) auto", detail);
    }

    [Fact]
    public void DetailPollingSurvivesTransientRefreshAndStopsOnlyAfterLoadedTerminalState()
    {
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");
        var start = detail.IndexOf("private async Task PollLoopAsync", StringComparison.Ordinal);
        var end = detail.IndexOf("public void Dispose", start, StringComparison.Ordinal);
        var polling = detail[start..end];

        Assert.Contains("TimeSpan.FromSeconds(3)", detail);
        Assert.Contains("var monitoringRender = IsActive", polling);
        Assert.Contains("monitoringRender || IsActive", polling);
        Assert.Contains("_job is not null && string.IsNullOrWhiteSpace(_loadError) && !IsActive", polling);
        Assert.Contains("polling will continue", polling);
        Assert.DoesNotContain("QueueRenderAsync", polling);
        Assert.Contains("StartPolling();", ExtractMethod(detail, "private async Task QueueRenderFromUserActionAsync"));
    }

    [Fact]
    public void QueueRenderUsesPerJobGateBeforeChargingAndEnqueueing()
    {
        var source = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");
        var queue = ExtractMethod(source, "public async Task<DanceSellJobDto> QueueRenderAsync");

        Assert.Contains("QueueLocks.GetOrAdd(id", source);
        Assert.Contains("await gate.WaitAsync(ct);", queue);
        Assert.Contains("finally", queue);
        Assert.Contains("gate.Release();", queue);
        Assert.Contains("DANCE_SELL_JOB_ALREADY_ACTIVE", queue);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method: {signature}");
        var end = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
    }

    private static string ReadRepoFile(params string[] path)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(path).ToArray()));
    }
}
