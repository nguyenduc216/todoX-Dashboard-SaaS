using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.Timelapse;
using Xunit;

namespace TodoX.Web.Tests;

public class TimelapsePhase2CTests
{
    [Fact]
    public void Workers_AreRegisteredAndUseRenderQueueGate()
    {
        var program = ReadSource("TodoX.Web", "Program.cs");
        var workers = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderWorkers.cs");

        Assert.Contains("AddHostedService<TodoX.Web.Services.Timelapse.TimelapseImageWorker>", program);
        Assert.Contains("AddHostedService<TodoX.Web.Services.Timelapse.TimelapseVideoWorker>", program);
        Assert.Contains("AddHostedService<TodoX.Web.Services.Timelapse.TimelapseFinalizerWorker>", program);
        Assert.Contains("RenderQueue:Enabled", workers);
        Assert.Contains("TimelapseProviderWorkerOptions", workers);
        Assert.Contains("\"TimelapseProviderWorkers\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
        Assert.Contains("\"DefaultImageSubmitPath\": \"/generateImage\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
        Assert.Contains("\"DefaultVideoSubmitPath\": \"/create-video\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_ClaimsUseSkipLockedAndPersistWorkerClaim()
    {
        var source = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkerRepository.cs");

        Assert.Contains("FOR UPDATE SKIP LOCKED", source);
        Assert.Contains("worker_claim", source);
        Assert.Contains("provider_task_id", source);
        Assert.Contains("AdvanceAfterImageCompletedAsync", source);
        Assert.Contains("AdvanceAfterVideoCompletedAsync", source);
    }

    [Fact]
    public void Runtime_Uses79AiCredentialResolverAndDoesNotUseYEScale()
    {
        var source = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");

        Assert.Contains("ResolveAsync(\"79ai\", \"access_token\"", source);
        Assert.Contains("Timelapse requires configured 79AI provider", source);
        Assert.Contains("/generateImage", source);
        Assert.Contains("/create-video", source);
        Assert.Contains("/image", source);
        Assert.Contains("/video", source);
        Assert.DoesNotContain("YEScale", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SubmitAndWaitAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_SubmitsWhenTaskMissingAndPollsExistingTask()
    {
        var source = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");

        Assert.Contains("if (string.IsNullOrWhiteSpace(item.ProviderTaskId))", source);
        Assert.Contains("SubmitImageAsync(item, ct)", source);
        Assert.Contains("SubmitVideoAsync(item, ct)", source);
        Assert.Contains("PollAsync(item.ProviderCode, item.ProviderTaskId", source);
        Assert.Contains("ReleaseImageClaimAsync", source);
        Assert.Contains("ReleaseVideoClaimAsync", source);
    }

    [Fact]
    public void Runtime_PersistsProviderOutputsToTodoXMedia()
    {
        var source = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");

        Assert.Contains("DownloadAndSaveImageAtObjectKeyAsync", source);
        Assert.Contains("DownloadAndSaveBinaryAtObjectKeyAsync", source);
        Assert.Contains("SaveImageCompletedAsync", source);
        Assert.Contains("SaveVideoCompletedAsync", source);
        Assert.Contains("never used as final customer media", ReadSource("TodoX.Web", "docs", "timelapse-phase-2c-provider-workers.md"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Finalizer_UsesOrderedClipsAndFfmpegConcatCopy()
    {
        var source = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseFinalizerRuntime.cs");
        var repo = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkerRepository.cs");

        Assert.Contains("OrderBy(x => x.ClipIndex)", source);
        Assert.Contains("-f", source);
        Assert.Contains("concat", source);
        Assert.Contains("-c", source);
        Assert.Contains("copy", source);
        Assert.Contains("ORDER BY clip_index", repo);
        Assert.Contains("SaveFinalizerCompletedAsync", source);
        Assert.Contains("Storage:Provider", source);
        Assert.Contains("requires local media storage", source);
    }

    [Fact]
    public void Runtime_UsesProviderSpecificImageFieldsWithoutGenericImagesPayloadForTwoImages()
    {
        var client = ReadSource("TodoX.Web", "Services", "AiProviders", "Ai79TaskClient.cs");
        var runtime = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");

        Assert.Contains("DefaultVideoStartImageField", runtime, StringComparison.Ordinal);
        Assert.Contains("DefaultVideoEndImageField", runtime, StringComparison.Ordinal);
        Assert.Contains("request.Images.Count > 2", client, StringComparison.Ordinal);
        Assert.DoesNotContain("FindString(document.RootElement, \"task_id\", \"taskId\", \"request_id\", \"requestId\", \"id\")", client, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderStatusNormalizer_MapsKnownStatuses()
    {
        Assert.Equal(Ai79TaskStatusNormalizer.Success, Ai79TaskStatusNormalizer.Normalize("SUCCESS"));
        Assert.Equal(Ai79TaskStatusNormalizer.Success, Ai79TaskStatusNormalizer.Normalize("completed"));
        Assert.Equal(Ai79TaskStatusNormalizer.Failed, Ai79TaskStatusNormalizer.Normalize("FAILURE"));
        Assert.Equal(Ai79TaskStatusNormalizer.Failed, Ai79TaskStatusNormalizer.Normalize("error"));
        Assert.Equal(Ai79TaskStatusNormalizer.Running, Ai79TaskStatusNormalizer.Normalize("pending"));
        Assert.Equal(Ai79TaskStatusNormalizer.Running, Ai79TaskStatusNormalizer.Normalize(null));
    }

    [Fact]
    public void PromptResolver_UsesProfileSnapshotAndStageProgress()
    {
        var snapshot = new TodoX.Web.Models.Timelapse.TimelapseJobSnapshot
        {
            ProfileName = "Nhà phố",
            ProfileCode = "townhouse",
            Ratio = TodoX.Web.Models.Timelapse.TimelapseRequestRules.LandscapeRatio
        };

        var prompt = TimelapsePromptResolver.ResolveImagePrompt(snapshot, 70,
            """{"ProfileJson":"Use existing n8n construction prompt semantics."}""");

        Assert.Contains("Use existing n8n construction prompt semantics.", prompt);
        Assert.Contains("70%", prompt);
        Assert.Contains("Nhà phố", prompt);
    }

    [Fact]
    public void Report_DocumentsRequiredPhase2CContracts()
    {
        var report = ReadSource("TodoX.Web", "docs", "timelapse-phase-2c-provider-workers.md");

        Assert.Contains("image provider path", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("video provider path", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("submit/poll", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worker claiming", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restart", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("media persistence", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("state advancement", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("finalizer", report, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSource(params string[] parts)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TodoX.Dashboard.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
